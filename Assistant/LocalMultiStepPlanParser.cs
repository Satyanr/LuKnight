namespace LuKnight.Assistant;

public static class LocalMultiStepPlanParser
{
    public const int MaxSteps =
        4;

    public const int MaxStepLength =
        500;

    public const int MaxInputLength =
        2000;

    private static readonly string[]
        Separators =
    [
        " setelah itu ",
        " and then ",
        " kemudian ",
        " lalu ",
        " then "
    ];

    public static AssistantPlanParseResult
        Parse(
            string? input)
    {
        if (string.IsNullOrWhiteSpace(
                input))
        {
            return AssistantPlanParseResult
                .NotRecognized();
        }

        string text =
            input.Trim();

        if (text.Length >
            MaxInputLength)
        {
            return AssistantPlanParseResult
                .Failure(
                    "Perintah multi-step terlalu panjang.");
        }

        // Padding preserves separator boundaries at either end after trimming.
        List<string> parts =
            Split(
                " " + text + " ",
                out bool recognized,
                out bool malformed);

        if (!recognized)
        {
            return AssistantPlanParseResult
                .NotRecognized();
        }

        if (malformed)
        {
            return AssistantPlanParseResult
                .Failure(
                    "Perintah multi-step memiliki langkah kosong atau pemisah yang tidak valid.");
        }

        if (parts.Count < 2)
        {
            return AssistantPlanParseResult
                .NotRecognized();
        }

        if (parts.Count > MaxSteps)
        {
            return AssistantPlanParseResult
                .Failure(
                    $"Lu-Knight membatasi satu rencana hingga {MaxSteps} langkah.");
        }

        var steps =
            new List<AssistantPlanStep>(
                parts.Count);

        for (int index = 0;
             index < parts.Count;
             index++)
        {
            string command =
                parts[index]
                    .Trim();

            if (command.Length == 0)
            {
                return AssistantPlanParseResult
                    .Failure(
                        "Rencana memiliki langkah kosong.");
            }

            if (command.Length >
                MaxStepLength)
            {
                return AssistantPlanParseResult
                    .Failure(
                        $"Langkah {index + 1} terlalu panjang.");
            }

            steps.Add(
                new AssistantPlanStep(
                    index,
                    command));
        }

        return AssistantPlanParseResult
            .Parsed(
                new AssistantPlan(
                    steps));
    }

    private static List<string>
        Split(
            string input,
            out bool recognized,
            out bool malformed)
    {
        recognized =
            false;

        malformed =
            false;

        var result =
            new List<string>();

        int segmentStart =
            0;

        int position =
            0;

        char quote =
            '\0';

        bool escaped =
            false;

        while (position <
               input.Length)
        {
            char current =
                input[position];

            if (escaped)
            {
                escaped =
                    false;

                position++;
                continue;
            }

            if (current == '\\' &&
                quote != '\0')
            {
                escaped =
                    true;

                position++;
                continue;
            }

            if (quote != '\0')
            {
                if (current ==
                    quote)
                {
                    quote =
                        '\0';
                }

                position++;
                continue;
            }

            if (current is
                '"' or '\'')
            {
                quote =
                    current;

                position++;
                continue;
            }

            string? separator =
                MatchSeparator(
                    input,
                    position);

            if (separator is null)
            {
                position++;
                continue;
            }

            recognized =
                true;

            string segment =
                input[
                    segmentStart..
                    position]
                .Trim();

            if (segment.Length == 0)
            {
                malformed =
                    true;

                return result;
            }

            result.Add(
                segment);

            // Reuse the boundary space to detect adjacent separators.
            position +=
                separator.Length - 1;

            segmentStart =
                position;
        }

        if (!recognized)
            return result;

        if (quote != '\0')
        {
            malformed = true;
            return result;
        }

        string final =
            input[
                segmentStart..]
            .Trim();

        if (final.Length == 0)
        {
            malformed =
                true;

            return result;
        }

        result.Add(
            final);

        return result;
    }

    private static string?
        MatchSeparator(
            string input,
            int index)
    {
        foreach (string separator
                 in Separators)
        {
            if (index +
                separator.Length >
                input.Length)
            {
                continue;
            }

            if (input.AsSpan(
                    index,
                    separator.Length)
                .Equals(
                    separator,
                    StringComparison
                        .OrdinalIgnoreCase))
            {
                return separator;
            }
        }

        return null;
    }
}
