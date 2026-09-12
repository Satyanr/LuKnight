using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuKnight.Services;

public sealed record MemoryEntry(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record MemoryDocument(
    int SchemaVersion,
    List<MemoryEntry> Items);

public sealed class MemoryService
{
    private const int CurrentSchemaVersion = 1;
    private const int MaxEntries = 200;
    private const int MaxTextLength = 500;
    private readonly string? _path;
    private readonly List<MemoryEntry> _items = new();

    public IReadOnlyList<MemoryEntry> Items => _items.AsReadOnly();
    public int Count => _items.Count;
    public bool IsPersistent => _path is not null;
    public string Status { get; private set; } = string.Empty;

    public MemoryService(string? path = null) => _path = path;

    public void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;

        try
        {
            if (new FileInfo(_path).Length > 1_048_576)
                throw new JsonException("Memory file terlalu besar.");

            MemoryDocument document = JsonSerializer.Deserialize<MemoryDocument>(
                File.ReadAllText(_path), SettingsService.JsonOptions)
                ?? throw new JsonException("Memory file kosong.");

            if (document.SchemaVersion > CurrentSchemaVersion)
            {
                Status = "Memory berasal dari versi aplikasi yang lebih baru.";
                return;
            }

            _items.Clear();
            foreach (MemoryEntry item in (document.Items ?? []).TakeLast(MaxEntries))
            {
                string text = Normalize(item.Text);
                if (_items.Any(existing => string.Equals(existing.Text, text, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _items.Add(item with { Text = text });
            }

            Status = "Long-term memory dimuat.";
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            TryBackupInvalidFile();
            _items.Clear();
            Status = "Memory tidak valid; menggunakan memory kosong.";
        }
    }

    public MemoryEntry Remember(string text)
    {
        text = Normalize(text);
        MemoryEntry? existing = _items.FirstOrDefault(item =>
            string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            MemoryEntry updated = existing with { UpdatedAt = DateTimeOffset.UtcNow };
            _items[_items.IndexOf(existing)] = updated;
            Save();
            return updated;
        }

        var entry = new MemoryEntry(Guid.NewGuid(), text, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _items.Add(entry);
        Trim();
        Save();
        return entry;
    }

    public bool ForgetExact(string text)
    {
        text = Normalize(text);
        int index = _items.FindIndex(item =>
            string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        Save();
        return true;
    }

    public IReadOnlyList<MemoryEntry> Search(string query, int maxResults = 6)
    {
        if (_items.Count == 0 || maxResults <= 0)
            return Array.Empty<MemoryEntry>();

        string[] tokens = Tokenize(query);
        bool genericMemoryQuestion = Regex.IsMatch(query, @"\b(ingat|memory|memori|remember)\b", RegexOptions.IgnoreCase);
        var matches = _items
            .Select(item => new { Item = item, Score = Score(item.Text, tokens) })
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Item.UpdatedAt)
            .Take(maxResults)
            .Select(result => result.Item)
            .ToArray();

        if (matches.Length > 0)
            return matches;

        return genericMemoryQuestion
            ? _items.OrderByDescending(item => item.UpdatedAt).Take(maxResults).ToArray()
            : Array.Empty<MemoryEntry>();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private bool Save()
    {
        if (_path is null)
        {
            Status = "Memory aktif selama sesi test.";
            return true;
        }

        string temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var document = new MemoryDocument(CurrentSchemaVersion, _items.ToList());
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, SettingsService.JsonOptions);
                stream.Flush(true);
            }

            if (File.Exists(_path))
                File.Replace(temporary, _path, _path + ".bak", true);
            else
                File.Move(temporary, _path);

            Status = "Long-term memory tersimpan.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Memory gagal disimpan; perubahan hanya berlaku sementara.";
            return false;
        }
    }

    private void Trim()
    {
        if (_items.Count > MaxEntries)
            _items.RemoveRange(0, _items.Count - MaxEntries);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Memory tidak boleh kosong.", nameof(value));

        string normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        if (normalized.Length > MaxTextLength)
            throw new ArgumentException($"Memory maksimal {MaxTextLength} karakter.", nameof(value));

        return normalized;
    }

    private static string[] Tokenize(string value) => Regex
        .Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}\-]+")
        .Select(match => match.Value)
        .Where(token => token.Length >= 3)
        .Distinct()
        .ToArray();

    private static int Score(string text, string[] queryTokens)
    {
        if (queryTokens.Length == 0)
            return 0;

        string candidate = text.ToLowerInvariant();
        return queryTokens.Count(token => candidate.Contains(token, StringComparison.Ordinal));
    }

    private void TryBackupInvalidFile()
    {
        if (_path is null || !File.Exists(_path))
            return;

        try
        {
            File.Copy(_path, _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
