using System.IO;
using System.Text.Json;
using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed record UserSkillLoadIssue(string FileName, string Message);
public sealed record UserSkillLoadResult(
    IReadOnlyList<IAssistantSkill> Skills,
    IReadOnlyList<UserSkillLoadIssue> Issues);

public sealed class UserSkillStore
{
    private readonly string _directory;
    public string DirectoryPath => _directory;
    public UserSkillStore(string? directory = null) =>
        _directory = directory ?? Path.Combine(SettingsService.UserDirectory, "Skills");

    public UserSkillLoadResult Load()
    {
        var skills = new List<IAssistantSkill>();
        var issues = new List<UserSkillLoadIssue>();
        try { Directory.CreateDirectory(_directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new("Skills", "Folder user skill tidak dapat dibuka."));
            return new(skills, issues);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(AssistantSkillPolicy.MaxFiles + 1).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new("Skills", "Daftar user skill tidak dapat dibaca."));
            return new(skills, issues);
        }
        if (files.Length > AssistantSkillPolicy.MaxFiles)
        {
            issues.Add(new("Skills", $"Maksimum {AssistantSkillPolicy.MaxFiles} file skill dapat dimuat."));
            files = files.Take(AssistantSkillPolicy.MaxFiles).ToArray();
        }
        foreach (string path in files) LoadOne(path, skills, issues);
        return new(skills.AsReadOnly(), issues.AsReadOnly());
    }

    private static void LoadOne(string path, ICollection<IAssistantSkill> skills,
        ICollection<UserSkillLoadIssue> issues)
    {
        string fileName = Path.GetFileName(path);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { issues.Add(new(fileName, "Symbolic link/reparse point tidak dimuat.")); return; }
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > AssistantSkillPolicy.MaxFileBytes)
            { issues.Add(new(fileName, "Ukuran file skill tidak valid.")); return; }
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            UserSkillDefinition? definition = JsonSerializer.Deserialize<UserSkillDefinition>(
                stream, SettingsService.JsonOptions);
            if (definition is null) throw new JsonException("Skill kosong.");
            string? validation = Validate(definition);
            if (validation is not null) { issues.Add(new(fileName, validation)); return; }
            if (!definition.Enabled) return;
            skills.Add(new UserDefinedAssistantSkill(definition));
        }
        catch (Exception ex) when (ex is JsonException or IOException or
            UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { issues.Add(new(fileName, $"Skill tidak dimuat: {ex.Message}")); }
    }

    internal static string? Validate(UserSkillDefinition value)
    {
        if (value.SchemaVersion != 1) return "Schema user skill tidak didukung.";
        if (!AssistantSkillPolicy.IsValidId(value.Id)) return "ID user skill tidak valid.";
        string display = value.DisplayName?.Trim() ?? string.Empty;
        if (display.Length is < 1 or > AssistantSkillPolicy.MaxDisplayNameLength ||
            AssistantSkillPolicy.HasControlCharacters(display)) return "Display name user skill tidak valid.";
        string description = value.Description?.Trim() ?? string.Empty;
        if (description.Length is < 1 or > AssistantSkillPolicy.MaxDescriptionLength ||
            AssistantSkillPolicy.HasControlCharacters(description)) return "Description user skill tidak valid.";
        if (value.Aliases is null || value.Aliases.Length > AssistantSkillPolicy.MaxAliases)
            return "Alias user skill tidak valid.";
        foreach (string? alias in value.Aliases)
            if (!AssistantSkillPolicy.IsValidId(alias)) return "Alias user skill tidak valid.";
        if (value.Steps is null || value.Steps.Length is < 1 or > LocalMultiStepPlanParser.MaxSteps)
            return $"User skill harus memiliki 1 sampai {LocalMultiStepPlanParser.MaxSteps} langkah.";
        int total = 0;
        foreach (string? raw in value.Steps)
        {
            string step = raw?.Trim() ?? string.Empty;
            if (step.Length is < 1 or > LocalMultiStepPlanParser.MaxStepLength)
                return "Langkah user skill tidak valid.";
            if (AssistantSkillPolicy.HasControlCharacters(step))
                return "Langkah user skill tidak boleh berisi karakter kontrol.";
            total += step.Length;
            if (total > LocalMultiStepPlanParser.MaxInputLength) return "Total user skill terlalu panjang.";
        }
        return null;
    }
}
