using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuKnight.Services;

public sealed record UpdateManifest(string Version, string Url, string Sha256, long Size);
public sealed class UpdateService
{
    public const string Repository = "Satyanr/LuKnight";
    public static string CurrentVersion => typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly string _directory;
    private readonly Func<ProcessStartInfo, bool> _launch;
    public UpdateManifest? Available { get; private set; }
    public string? VerifiedInstaller { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Belum memeriksa pembaruan.";
    public UpdateService(SettingsService settings, HttpClient? client = null, string? directory = null, Func<ProcessStartInfo, bool>? launch = null)
    {
        _settings = settings; _http = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _directory = directory ?? Path.Combine(SettingsService.UserDirectory, "Updates");
        _launch = launch ?? (info => { using var process = Process.Start(info); return process is not null; });
    }
    public static Version ParseVersion(string version)
    {
        if (!Regex.IsMatch(version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")) throw new InvalidDataException("Versi release tidak valid.");
        return Version.Parse(version);
    }
    private static Uri AssetUri(string url, string version, string filename)
    {
        string expected = $"https://github.com/{Repository}/releases/download/v{version}/{filename}";
        if (url != expected) throw new InvalidDataException("Sumber update tidak sesuai repository resmi.");
        return new Uri(url);
    }
    private async Task<byte[]> Fetch(Uri uri, long limit, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("LuKnight/" + CurrentVersion);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Metadata terlalu besar.");
        using var source = await response.Content.ReadAsStreamAsync(token); using var result = new MemoryStream();
        byte[] buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        { if (result.Length + read > limit) throw new InvalidDataException("Metadata terlalu besar."); await result.WriteAsync(buffer.AsMemory(0, read), token); }
        return result.ToArray();
    }
    public async Task<UpdateManifest?> CheckForUpdate(bool automatic = false, CancellationToken token = default)
    {
        if (IsBusy) return Available;
        if (automatic && _settings.Current.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return Available;
        IsBusy = true; Status = "Memeriksa GitHub Releases…";
        try
        {
            _settings.Update(_settings.Current with { LastUpdateCheck = DateTimeOffset.UtcNow });
            byte[] release;
            try { release = await Fetch(new Uri($"https://api.github.com/repos/{Repository}/releases/latest"), 1_048_576, token); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            { Available = null; VerifiedInstaller = null; Status = "Belum ada release publik."; return null; }
            using var doc = JsonDocument.Parse(release); var root = doc.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("Release belum stabil.");
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v')) throw new InvalidDataException("Tag release tidak valid.");
            string version = tag[1..]; var latest = ParseVersion(version);
            if (latest <= ParseVersion(CurrentVersion)) { Available = null; VerifiedInstaller = null; Status = "Lu-Knight sudah menggunakan versi terbaru."; return null; }
            string url = root.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "update.json").GetProperty("browser_download_url").GetString()!;
            byte[] data = await Fetch(AssetUri(url, version, "update.json"), 16_384, token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(data, SettingsService.JsonOptions) ?? throw new InvalidDataException();
            Validate(manifest);
            if (manifest.Version != version) throw new InvalidDataException("Versi manifest berbeda dengan release.");
            if (Available != manifest) VerifiedInstaller = null;
            Available = manifest; Status = $"Lu-Knight {version} tersedia. Versi saat ini: {CurrentVersion}."; return manifest;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Pemeriksaan dibatalkan."; throw; }
        catch (OperationCanceledException) { Status = "Waktu pemeriksaan update habis. Coba lagi nanti."; return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or TaskCanceledException)
        { Status = "Pembaruan belum dapat diperiksa. Periksa koneksi atau kelengkapan release, lalu coba lagi."; return null; }
        finally { IsBusy = false; }
    }
    public Task<UpdateManifest?> GetLatestVersion(CancellationToken token = default) => CheckForUpdate(false, token);
    public static void Validate(UpdateManifest manifest)
    {
        ParseVersion(manifest.Version); AssetUri(manifest.Url, manifest.Version, "LuKnightSetup.exe");
        if (manifest.Sha256 is null || !Regex.IsMatch(manifest.Sha256, @"\A[0-9a-fA-F]{64}\z") || manifest.Size is <= 0 or > 536_870_912)
            throw new InvalidDataException("Checksum atau ukuran installer tidak valid.");
    }
    public async Task<bool> DownloadUpdate(CancellationToken token = default)
    {
        if (IsBusy || Available is null) return false;
        IsBusy = true; VerifiedInstaller = null; string? partial = null;
        try
        {
            var manifest = Available; Validate(manifest); Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "LuKnight-" + manifest.Version + "-Setup.exe"); partial = path + ".part";
            using var request = new HttpRequestMessage(HttpMethod.Get, manifest.Url); request.Headers.UserAgent.ParseAdd("LuKnight/" + CurrentVersion);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != manifest.Size) throw new InvalidDataException();
            using (var input = await response.Content.ReadAsStreamAsync(token))
            using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += read; if (total > manifest.Size) throw new InvalidDataException();
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    Status = $"Mengunduh update… {total * 100 / manifest.Size}%";
                }
                await output.FlushAsync(token);
            }
            if (!await VerifyUpdate(partial, manifest, token)) throw new InvalidDataException();
            File.Move(partial, path, true); VerifiedInstaller = path; Status = "Update terverifikasi. Siap dipasang setelah aktivitas selesai."; return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Unduhan dibatalkan."; throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException)
        { Status = "Unduhan gagal atau checksum tidak cocok. Installer tidak dijalankan; versi saat ini tetap tersedia."; return false; }
        finally
        {
            if (partial is not null) { try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            IsBusy = false;
        }
    }
    public static async Task<bool> VerifyUpdate(string path, UpdateManifest manifest, CancellationToken token = default)
    {
        Validate(manifest);
        using var stream = File.OpenRead(path);
        if (stream.Length != manifest.Size) return false;
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public async Task<bool> InstallUpdate(Func<bool> canInstall, Func<bool> save, Action shutdown, CancellationToken token = default)
    {
        if (IsBusy || VerifiedInstaller is null || Available is null || !canInstall()) { Status = "Tunggu drag, jatuh, atau permintaan AI selesai sebelum memasang update."; return false; }
        IsBusy = true;
        try
        {
            if (!await VerifyUpdate(VerifiedInstaller, Available, token)) { VerifiedInstaller = null; throw new InvalidDataException(); }
            if (!canInstall() || !save()) { Status = "Aktivitas belum selesai atau pengaturan belum tersimpan. Update ditunda."; return false; }
            var start = new ProcessStartInfo(VerifiedInstaller) { UseShellExecute = true };
            start.ArgumentList.Add("/UPDATE"); start.ArgumentList.Add("/TARGETPID=" + Environment.ProcessId);
            start.ArgumentList.Add("/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (!_launch(start)) throw new IOException();
            shutdown(); return true; // Installer waits for this process before replacing files.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Pemasangan dibatalkan."; throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Status = "Installer tidak dapat dimulai. Aplikasi tetap berjalan."; return false; }
        finally { IsBusy = false; }
    }
}
