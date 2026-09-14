using LuKnight.Assistant;

internal static partial class Program
{
    private static void
        CheckPlannerParser()
    {
        AssistantPlanParseResult id =
            LocalMultiStepPlanParser.Parse(
                "buka chrome lalu buka downloads");

        Require(
            id.Success &&
            id.Plan?.Count == 2,
            "Indonesian multi-step command was not parsed.");

        Require(
            id.Plan!.Steps[0].Command ==
                "buka chrome" &&
            id.Plan.Steps[1].Command ==
                "buka downloads",
            "Plan steps changed unexpectedly.");


        AssistantPlanParseResult english =
            LocalMultiStepPlanParser.Parse(
                "open chrome then open downloads");

        Require(
            english.Success &&
            english.Plan?.Count == 2,
            "English multi-step command was not parsed.");


        AssistantPlanParseResult sequential =
            LocalMultiStepPlanParser.Parse(
                "buka chrome kemudian buka downloads setelah itu fokus window chrome");

        Require(
            sequential.Success &&
            sequential.Plan?.Count == 3,
            "Three-step plan was not parsed.");


        AssistantPlanParseResult quoted =
            LocalMultiStepPlanParser.Parse(
                "isi textbox Notes dengan \"beli kopi lalu pulang\" di window Notes lalu klik tombol Refresh di window Notes");

        Require(
            quoted.Success &&
            quoted.Plan?.Count == 2,
            "Separator inside quotes incorrectly split the plan.");

        Require(
            quoted.Plan!.Steps[0]
                .Command
                .Contains(
                    "\"beli kopi lalu pulang\"",
                    StringComparison.Ordinal),
            "Quoted text changed during plan parsing.");


        AssistantPlanParseResult single =
            LocalMultiStepPlanParser.Parse(
                "buka chrome");

        Require(
            !single.Recognized,
            "Single command was incorrectly treated as a plan.");


        AssistantPlanParseResult bareAnd =
            LocalMultiStepPlanParser.Parse(
                "isi textbox Nama dengan Budi dan Andi di window Form");

        Require(
            !bareAnd.Recognized,
            "Bare 'dan' incorrectly created a multi-step plan.");


        AssistantPlanParseResult empty =
            LocalMultiStepPlanParser.Parse(
                "buka chrome lalu ");

        Require(
            empty.Recognized &&
            !empty.Success,
            "Empty final step was accepted.");


        AssistantPlanParseResult leading =
            LocalMultiStepPlanParser.Parse(
                "lalu buka chrome");

        Require(
            !leading.Success,
            "Leading separator was accepted.");


        AssistantPlanParseResult tooMany =
            LocalMultiStepPlanParser.Parse(
                "buka chrome lalu buka downloads lalu buka documents lalu buka pictures lalu buka music");

        Require(
            tooMany.Recognized &&
            !tooMany.Success,
            "Plan above maximum step count was accepted.");


        string oversizedStep =
            new(
                'a',
                LocalMultiStepPlanParser
                    .MaxStepLength +
                1);

        AssistantPlanParseResult longStep =
            LocalMultiStepPlanParser.Parse(
                $"buka chrome lalu {oversizedStep}");

        Require(
            longStep.Recognized &&
            !longStep.Success,
            "Oversized plan step was accepted.");


        AssistantPlanParseResult unicode =
            LocalMultiStepPlanParser.Parse(
                "isi textbox Notes dengan \"Halo Ω 日本語\" di window Notes lalu klik tombol Refresh di window Notes");

        Require(
            unicode.Success &&
            unicode.Plan!.Steps[0]
                .Command
                .Contains(
                    "Ω 日本語",
                    StringComparison.Ordinal),
            "Unicode plan content changed.");


        // Parser hanya mengenali struktur.
        // Ia TIDAK memberi authorization.
        AssistantPlanParseResult dangerous =
            LocalMultiStepPlanParser.Parse(
                "buka chrome lalu jalankan powershell");

        Require(
            dangerous.Success,
            "Syntax parser should not pretend to be the security policy.");

        Require(
            dangerous.Plan!.Steps[1]
                .Command ==
                "jalankan powershell",
            "Planner altered raw command before security routing.");
        foreach (string malformed in new[] { "lalu buka chrome", "buka chrome lalu", "buka chrome lalu lalu buka downloads", "buka chrome lalu \"unfinished" })
        {
            var result = LocalMultiStepPlanParser.Parse(malformed);
            Require(result.Recognized && !result.Success, "Malformed plan was not rejected: " + malformed);
        }
        foreach (string separator in new[] { "lalu", "kemudian", "setelah itu", "then", "and then", "THEN" })
        {
            var result = LocalMultiStepPlanParser.Parse($"  first {separator} second  ");
            Require(result.Success && result.Plan!.Steps.Select(step => step.Index).SequenceEqual(new[] { 0, 1 }), "Separator or step indices invalid.");
        }
        Require(LocalMultiStepPlanParser.Parse("a lalu b lalu c lalu d").Plan?.Count == 4, "Four-step boundary rejected.");
        Require(LocalMultiStepPlanParser.Parse(new string('a', 500) + " lalu b").Success, "500-character step rejected.");
        Require(!LocalMultiStepPlanParser.Parse(new string('a', 2001)).Success, "Oversized input accepted.");
        Require(LocalMultiStepPlanParser.Parse(string.Join(" lalu ", new string('a', 500), new string('b', 500), new string('c', 500), new string('d', 482))).Success, "2000-character input rejected.");
        foreach (string? singleInput in new[] { null, "", "  ", "Budi and Andi", "write \"then lalu\"" })
            Require(!LocalMultiStepPlanParser.Parse(singleInput).Recognized, "Single input recognized as plan.");
        string escaped = "write \"say \\\"then\\\" lalu\" then next";
        var quotedEscaped = LocalMultiStepPlanParser.Parse(escaped);
        Require(quotedEscaped.Success && quotedEscaped.Plan!.Count == 2 && quotedEscaped.Plan.Steps[0].Command == escaped[..escaped.LastIndexOf(" then ", StringComparison.Ordinal)], "Escaped quote content changed.");
        Require(LocalMultiStepPlanParser.Parse("write 'then lalu' then next").Plan?.Count == 2, "Single quotes split incorrectly.");
        string ordinaryLongChat =
            new(
                'x',
                LocalMultiStepPlanParser
                    .MaxInputLength +
                500);

        AssistantPlanParseResult ordinaryLong =
            LocalMultiStepPlanParser.Parse(
                ordinaryLongChat);

        Require(
            !ordinaryLong.Recognized,
            "Long ordinary chat was hijacked by planner.");
        string oversizedPlan =
            new string(
                'x',
                LocalMultiStepPlanParser
                    .MaxInputLength) +
            " lalu buka chrome";

        AssistantPlanParseResult oversized =
            LocalMultiStepPlanParser.Parse(
                oversizedPlan);

        Require(
            oversized.Recognized &&
            !oversized.Success,
            "Oversized real plan was not rejected.");
    }
}
