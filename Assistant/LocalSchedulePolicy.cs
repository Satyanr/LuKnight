namespace LuKnight.Assistant;

public static class LocalSchedulePolicy
{
    public const int MaxSchedules =
        128;

    public const int MaxDisplayNameLength =
        100;

    public const int MaxArgumentLength =
        500;

    public const int MaxParameters =
        AssistantSkillPolicy.MaxParameters;

    public const int MaxParameterValueLength =
        AssistantSkillPolicy.MaxParameterValueLength;

    public const int MaxParameterTotalLength =
        AssistantSkillPolicy.MaxParameterTotalLength;


    public static bool TryNormalize(
        ScheduledSkill source,
        out ScheduledSkill normalized,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        normalized =
            default!;

        error =
            string.Empty;


        if (source.Id ==
            Guid.Empty)
        {
            error =
                "ID jadwal tidak valid.";

            return false;
        }


        string displayName =
            source.DisplayName?.Trim() ??
            string.Empty;

        if (displayName.Length is < 1 or >
            MaxDisplayNameLength ||
            displayName.Any(
                char.IsControl))
        {
            error =
                "Nama jadwal tidak valid.";

            return false;
        }


        ScheduledSkillInvocation invocation =
            source.Invocation ??
            new ScheduledSkillInvocation();


        string skillId =
            invocation.SkillId?.Trim() ??
            string.Empty;

        if (!AssistantSkillPolicy
                .IsValidId(
                    skillId) ||
            skillId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error =
                "ID skill jadwal tidak valid.";

            return false;
        }


        string argument =
            invocation.Argument?.Trim() ??
            string.Empty;

        if (argument.Length >
                MaxArgumentLength ||
            argument.Any(
                char.IsControl))
        {
            error =
                "Argument jadwal tidak valid.";

            return false;
        }


        IReadOnlyDictionary<string, string>?
            parameters =
                NormalizeParameters(
                    invocation.Parameters,
                    out string parameterError);

        if (parameterError.Length >
            0)
        {
            error =
                parameterError;

            return false;
        }


        if (argument.Length > 0 &&
            parameters is
            { Count: > 0 })
        {
            error =
                "Jadwal tidak boleh memakai argument legacy dan named parameters sekaligus.";

            return false;
        }


        DateTimeOffset due =
            source.DueAtUtc
                .ToUniversalTime();

        DateTimeOffset created =
            source.CreatedAtUtc
                .ToUniversalTime();


        normalized =
            source with
            {
                DisplayName =
                    displayName,

                DueAtUtc =
                    due,

                CreatedAtUtc =
                    created,

                LastPresentedAtUtc =
                    source.LastPresentedAtUtc?
                        .ToUniversalTime(),

                Invocation =
                    new ScheduledSkillInvocation
                    {
                        SkillId =
                            skillId,

                        Argument =
                            argument,

                        Parameters =
                            parameters
                    }
            };

        return true;
    }


    private static IReadOnlyDictionary<string, string>?
        NormalizeParameters(
            IReadOnlyDictionary<string, string>?
                source,
            out string error)
    {
        error =
            string.Empty;

        if (source is null ||
            source.Count == 0)
        {
            return null;
        }


        if (source.Count >
            MaxParameters)
        {
            error =
                "Jumlah parameter jadwal terlalu banyak.";

            return null;
        }


        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        int total =
            0;


        foreach ((string rawName, string rawValue)
                 in source)
        {
            string name =
                rawName?.Trim() ??
                string.Empty;

            string value =
                rawValue?.Trim() ??
                string.Empty;


            if (!AssistantSkillPolicy
                    .IsValidParameterName(
                        name) ||
                AssistantSkillPolicy
                    .IsReservedParameterName(
                        name))
            {
                error =
                    $"Nama parameter jadwal '{name}' tidak valid.";

                return null;
            }


            if (value.Length >
                    MaxParameterValueLength ||
                value.Any(
                    char.IsControl))
            {
                error =
                    $"Nilai parameter '{name}' tidak valid.";

                return null;
            }


            total +=
                value.Length;

            if (total >
                MaxParameterTotalLength)
            {
                error =
                    "Total parameter jadwal terlalu panjang.";

                return null;
            }


            if (!result.TryAdd(
                    name,
                    value))
            {
                error =
                    $"Parameter '{name}' duplikat.";

                return null;
            }
        }


        return new
            System.Collections.ObjectModel
            .ReadOnlyDictionary<string, string>(
                result);
    }
}
