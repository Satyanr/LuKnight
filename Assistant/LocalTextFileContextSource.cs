using System.IO;
using System.Text;
using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class LocalTextFileContextSource : IAssistantContextSource
{
    private const long MaxFileBytes = 512 * 1024;
    private const int MaxContextChars = 24_000;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".config",
            ".cs", ".xaml", ".js", ".ts", ".jsx", ".tsx", ".html", ".htm", ".css", ".scss",
            ".sql", ".py", ".php", ".java", ".c", ".cpp", ".h", ".hpp", ".ps1", ".bat", ".cmd", ".sh"
        };

    private static readonly HashSet<string> SensitiveFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".env",
            "credentials.json",
            "secrets.json",
            "id_rsa",
            "id_ed25519"
        };

    private readonly Func<bool> _enabled;

    public string Name => BuiltInContextNames.LocalTextFile;

    public LocalTextFileContextSource(Func<bool> enabled)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
    }

    public async Task<ContextCaptureResult> CaptureAsync(
        ContextInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return new ContextCaptureResult(false, "File context sedang nonaktif. Aktifkan melalui Settings → AI & Chat.");
        }

        if (!invocation.Arguments.TryGetValue("path", out string? suppliedPath) || string.IsNullOrWhiteSpace(suppliedPath))
        {
            return new ContextCaptureResult(false, "Path file tidak tersedia.");
        }

        string fullPath;
        try
        {
            if (!Path.IsPathFullyQualified(suppliedPath))
                return new ContextCaptureResult(false, "Gunakan absolute path untuk file.");

            fullPath = Path.GetFullPath(suppliedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ContextCaptureResult(false, "Path file tidak valid.");
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return new ContextCaptureResult(false, "Network path belum didukung.");

        if (!File.Exists(fullPath))
            return new ContextCaptureResult(false, "File tidak ditemukan.");

        string fileName = Path.GetFileName(fullPath);
        string extension = Path.GetExtension(fullPath);

        if (SensitiveFileNames.Contains(fileName) ||
            extension is ".pem" or ".key" or ".pfx" or ".p12" or ".kdbx")
        {
            return new ContextCaptureResult(false, "File ini termasuk jenis yang berpotensi menyimpan credential atau secret.");
        }

        if (!AllowedExtensions.Contains(extension))
        {
            return new ContextCaptureResult(false, $"Format '{extension}' belum didukung oleh File Context.");
        }

        try
        {
            // A parent junction can redirect an otherwise ordinary-looking local path.
            for (FileSystemInfo? entry = new FileInfo(fullPath); entry is not null;
                 entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new ContextCaptureResult(false, "Symbolic link / reparse point belum didukung.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ContextCaptureResult(false, "Metadata file tidak dapat dibaca.");
        }

        string content;
        try
        {
            // Check the opened handle, and cap the read even if an existing writer grows the file.
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxFileBytes)
                return new ContextCaptureResult(false, "File terlalu besar. Maksimal 512 KB.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            char[] buffer = new char[MaxContextChars + 1];
            int count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            content = new string(buffer, 0, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return new ContextCaptureResult(false, "Isi file tidak dapat dibaca.");
        }

        if (content.IndexOf('\0') >= 0)
            return new ContextCaptureResult(false, "File tampaknya bukan text file.");

        bool truncated = content.Length > MaxContextChars;
        if (truncated)
            content = content[..MaxContextChars];

        var reference = new ChatReferenceBlock(
            Kind: "local-text-file",
            Name: fileName,
            Content: content,
            Truncated: truncated);

        return new ContextCaptureResult(
            true,
            truncated ? $"File '{fileName}' dimuat sebagian." : $"File '{fileName}' dimuat.",
            reference);
    }
}
