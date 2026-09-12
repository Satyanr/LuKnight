using System.Net.Http;
using LuKnight.Models;

namespace LuKnight.Services;

public sealed class ChatCoordinator : IChatService
{
    private readonly ICredentialService _credentials;
    private readonly Func<string?> _environment;
    private readonly HttpClient? _client;
    private string? _key;
    private GeminiChatService _gemini;
    public ChatSettings Options { get; private set; }
    public bool IsBusy { get; private set; }
    public bool HasKey => !string.IsNullOrWhiteSpace(_key);
    public bool UsesGemini => Options.Provider == ChatProvider.Gemini && HasKey;
    public bool LastReplyWasGemini { get; private set; }
    public string CredentialStatus { get; private set; } = "";
    public string Status { get; private set; } = "Belum diuji.";
    public string DisplayName => UsesGemini ? $"Gemini · {Options.Model}" : "Local fallback";
    public ChatCoordinator(ICredentialService credentials, ChatSettings options, Func<string?>? environment = null, HttpClient? client = null)
    {
        _credentials = credentials; _environment = environment ?? (() => Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        _client = client; Options = options;
        _gemini = new(() => _key, options, client); RefreshCredentials();
    }
    public void Configure(ChatSettings options)
    {
        if (IsBusy) throw new InvalidOperationException("Tunggu permintaan chat selesai.");
        if (options == Options) return;
        if (options.Provider != Options.Provider || options.Model != Options.Model) _gemini.ClearConversation();
        Options = options; _gemini.Configure(options); Status = "Preferensi chat diperbarui; koneksi belum diuji.";
    }
    public void RefreshCredentials()
    {
        try
        {
            _key = _credentials.Read();
            if (!string.IsNullOrWhiteSpace(_key)) CredentialStatus = "•••••••• · Windows Credential Manager";
            else { _key = _environment()?.Trim(); CredentialStatus = HasKey ? "•••••••• · environment fallback" : "API key belum tersedia."; }
        }
        catch (System.ComponentModel.Win32Exception) { _key = _environment()?.Trim(); CredentialStatus = HasKey ? "Credential Manager tidak tersedia; environment fallback aktif." : "Credential Manager tidak dapat dibaca."; }
        Status = UsesGemini ? "Gemini dikonfigurasi; koneksi belum diuji." : "Local fallback aktif.";
    }
    public void UpdateKey(string key)
    { EnsureIdle(); _credentials.Write(key); _gemini.ClearConversation(); RefreshCredentials(); }
    public void RemoveKey()
    { EnsureIdle(); _credentials.Remove(); _gemini.ClearConversation(); RefreshCredentials(); }
    public void ClearConversation() { EnsureIdle(); _gemini.ClearConversation(); Status = "Percakapan dihapus."; }
    private void EnsureIdle() { if (IsBusy) throw new InvalidOperationException("Tunggu permintaan chat selesai."); }
    public async Task<bool> TestConnection(CancellationToken token = default)
    {
        EnsureIdle();
        if (!UsesGemini) { Status = "Gemini tidak aktif atau API key belum tersedia. Local fallback aktif."; return false; }
        IsBusy = true;
        try
        {
            var probe = new GeminiChatService(() => _key, Options with { RememberConversation = false, ResponseLength = ResponseLength.Short }, _client);
            await probe.SendMessageAsync("Reply with OK.", token); Status = "Gemini connected · koneksi berhasil diuji."; return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Tes koneksi dibatalkan."; throw; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        { Status = "AI unavailable · Local fallback aktif. " + SafeError(ex); return false; }
        finally { IsBusy = false; }
    }
    public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default) =>
        SendMessageAsync(message, assistantInstruction: null, cancellationToken);

    public async Task<string> SendMessageAsync(string message, string? assistantInstruction, CancellationToken cancellationToken = default)
    {
        EnsureIdle(); IsBusy = true; LastReplyWasGemini = false;
        try
        {
            if (UsesGemini)
            {
                try
                {
                    string answer = await _gemini.SendMessageAsync(message, assistantInstruction, cancellationToken);
                    LastReplyWasGemini = true; Status = "Gemini connected."; return answer;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
                { Status = "AI unavailable · Local fallback aktif. " + SafeError(ex); }
            }
            else Status = "Local fallback aktif.";
            cancellationToken.ThrowIfCancellationRequested();
            return LocalReply(message, Options);
        }
        finally { IsBusy = false; }
    }
    private static string SafeError(Exception ex) => ex is TimeoutException ? "Waktu koneksi habis." : "Periksa koneksi, key, kuota, atau model.";
    public static string LocalReply(string message, ChatSettings options)
    {
        bool english = options.Language == ChatLanguage.English || (options.Language == ChatLanguage.Automatic &&
            System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(hello|please|what|how|help|thanks)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        string reply = english ? "I'm here with you. Online AI is unavailable; local mode can acknowledge your messages but cannot generate an AI answer."
            : "Aku menemanimu di sini. AI online belum tersedia; mode lokal dapat menerima pesan, tetapi belum bisa memberi jawaban AI.";
        if (options.ResponseLength == ResponseLength.Short) reply = english ? "I'm here. Local mode is active; online AI is unavailable." : "Aku di sini. Mode lokal aktif; AI online belum tersedia.";
        if (options.ResponseLength == ResponseLength.Detailed) reply += english ? " Open Settings → AI & Chat to configure Gemini and test the connection." : " Buka Settings → AI & Chat untuk mengatur Gemini dan menguji koneksi.";
        if (options.Style == ResponseStyle.Playful) reply += " ✨";
        return reply;
    }
}
