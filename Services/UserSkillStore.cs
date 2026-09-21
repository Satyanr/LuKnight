using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed record UserSkillLoadIssue(string FileName, string Message);
public sealed record UserSkillLoadResult(
    IReadOnlyList<IAssistantSkill> Skills,
    IReadOnlyList<UserSkillLoadIssue> Issues);

public sealed class UserSkillStore
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{([a-z][a-z0-9_-]{0,31}|argument)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        if (value.SchemaVersion is not 1 and not 2)
            return "Schema user skill tidak didukung.";
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
        if (value.Parameters is null) return "Named parameters user skill tidak valid.";
        if (value.SchemaVersion == 1 && value.Parameters.Length > 0)
            return "Schema 1 tidak mendukung named parameters.";
        HashSet<string>? declared = null;
        if (value.SchemaVersion == 2)
        {
            if (value.Parameters.Length is < 1 or > AssistantSkillPolicy.MaxParameters)
                return "Named parameters user skill tidak valid.";
            declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UserSkillParameterDefinition parameter in value.Parameters)
            {
                string name = parameter.Name?.Trim() ?? string.Empty;
                if (!AssistantSkillPolicy.IsValidParameterName(name) ||
                    AssistantSkillPolicy.IsReservedParameterName(name))
                    return $"Nama parameter '{name}' tidak valid.";
                if (!declared.Add(name)) return $"Parameter '{name}' terdaftar lebih dari sekali.";
                if (parameter.MaxLength is < 1 or > AssistantSkillPolicy.MaxParameterValueLength)
                    return $"Batas panjang parameter '{name}' tidak valid.";
            }
        }
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
            MatchCollection placeholders = PlaceholderPattern.Matches(step);
            string withoutKnown = PlaceholderPattern.Replace(step, string.Empty);
            if (withoutKnown.Contains('{') || withoutKnown.Contains('}'))
                return "Placeholder workflow tidak valid.";
            foreach (Match placeholder in placeholders)
            {
                string name = placeholder.Groups[1].Value;
                if (value.SchemaVersion == 1 &&
                    !string.Equals(name, "argument", StringComparison.OrdinalIgnoreCase))
                    return "Schema 1 hanya mendukung placeholder {argument}.";
                if (value.SchemaVersion == 2)
                {
                    if (string.Equals(name, "argument", StringComparison.OrdinalIgnoreCase))
                        return "Schema 2 tidak mendukung placeholder legacy {argument}.";
                    if (!declared!.Contains(name))
                        return $"Placeholder '{{{name}}}' tidak memiliki definisi parameter.";
                }
            }
            total += step.Length;
            if (total > LocalMultiStepPlanParser.MaxInputLength) return "Total user skill terlalu panjang.";
        }
        if (value.SchemaVersion == 2)
        {
            string combined = string.Join("\n", value.Steps);
            foreach (UserSkillParameterDefinition parameter in value.Parameters)
                if (!combined.Contains($"{{{parameter.Name}}}", StringComparison.OrdinalIgnoreCase))
                    return $"Parameter '{parameter.Name}' tidak digunakan oleh workflow.";
        }
        return null;
    }
}
