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
    private bool _readOnly;
    private readonly List<MemoryEntry> _items = new();

    public IReadOnlyList<MemoryEntry> Items => _items.AsReadOnly();
    public int Count => _items.Count;
    public bool IsPersistent => _path is not null;
    public string Status { get; private set; } = string.Empty;

    public MemoryService(string? path = null) => _path = path;

    public void Load()
    {
        if (_path is null)
            return;


        _readOnly =
            false;

        _items.Clear();


        CommittedStateRecovery
            .DeleteUncommittedTemporary(
                _path);


        CommittedStateCandidateStatus
            primary =
                TryReadCandidate(
                    _path,
                    out List<MemoryEntry>
                        primaryItems);


        if (primary ==
            CommittedStateCandidateStatus.Valid)
        {
            _items.AddRange(
                primaryItems);

            Status =
                "Long-term memory dimuat.";

            return;
        }


        if (primary ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            Status =
                "Memory berasal dari versi aplikasi yang lebih baru.";

            return;
        }


        string backupPath =
            CommittedStateRecovery
                .BackupPath(
                    _path);


        CommittedStateCandidateStatus
            backup =
                TryReadCandidate(
                    backupPath,
                    out List<MemoryEntry>
                        backupItems);


        if (backup ==
            CommittedStateCandidateStatus.Valid)
        {
            _items.AddRange(
                backupItems);


            if (CommittedStateRecovery
                    .TryRestoreValidatedBackup(
                        _path))
            {
                Status =
                    "Long-term memory dipulihkan dari backup terakhir.";
            }
            else
            {
                _readOnly =
                    true;

                Status =
                    "Long-term memory dipulihkan sementara dari backup; " +
                    "penyimpanan tetap read-only.";
            }


            return;
        }


        if (backup ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            Status =
                "Backup memory berasal dari versi aplikasi lebih baru.";

            return;
        }


        if (primary ==
            CommittedStateCandidateStatus.Invalid)
        {
            if (!CommittedStateRecovery
                    .TryPreserveInvalidPrimary(
                        _path))
            {
                _readOnly =
                    true;
            }


            Status =
                "Memory tidak valid dan backup valid tidak tersedia; " +
                "menggunakan memory kosong.";
        }
        else
        {
            _readOnly =
                primary ==
                CommittedStateCandidateStatus
                    .Inaccessible;

            Status =
                _readOnly
                    ? "Memory tidak dapat dibaca; menggunakan memory kosong sementara."
                    : string.Empty;
        }
    }

    private
        CommittedStateCandidateStatus
        TryReadCandidate(
            string path,
            out List<MemoryEntry> items)
    {
        items =
            [];


        if (!File.Exists(
                path))
        {
            return
                CommittedStateCandidateStatus
                    .Missing;
        }


        try
        {
            if (new FileInfo(
                    path)
                .Length >
                1_048_576)
            {
                return
                    CommittedStateCandidateStatus
                        .Invalid;
            }


            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            int schema = 0;
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
                if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
                    schema = property.Value.GetInt32();
            if (schema > CurrentSchemaVersion)
                return CommittedStateCandidateStatus.FutureVersion;
            MemoryDocument document = JsonSerializer.Deserialize<MemoryDocument>(json.RootElement.GetRawText(),
                SettingsService.JsonOptions) ?? throw new JsonException();

            var loaded =
                new List<MemoryEntry>();


            foreach (MemoryEntry item
                     in (document.Items ?? [])
                         .TakeLast(
                             MaxEntries))
            {
                if (item is null)
                {
                    throw new JsonException();
                }


                string text =
                    Normalize(
                        item.Text);


                if (loaded.Any(
                        existing =>
                            string.Equals(
                                existing.Text,
                                text,
                                StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }


                loaded.Add(
                    item with
                    {
                        Text =
                            text
                    });
            }


            items =
                loaded;


            return
                CommittedStateCandidateStatus
                    .Valid;
        }
        catch (Exception ex)
            when (ex is
                JsonException or
                ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            return
                CommittedStateCandidateStatus
                    .Invalid;
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            return
                CommittedStateCandidateStatus
                    .Inaccessible;
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
        bool saved = Save();
        if (!saved)
            return;

        Status = IsPersistent
            ? "Semua long-term memory telah dihapus."
            : "Long-term memory sesi uji telah dikosongkan.";
    }

    private bool Save()
    {
        if (_path is null)
        {
            Status = "Memory aktif selama sesi test.";
            return true;
        }

        if (_readOnly)
        {
            Status = "File memory dipertahankan; perubahan hanya berlaku selama sesi ini.";
            return false;
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
            CommittedStateRecovery.DeleteUncommittedTemporary(_path);
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

}
