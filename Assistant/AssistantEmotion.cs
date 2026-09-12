using System.Text.RegularExpressions;
using LuKnight.Models;

namespace LuKnight.Assistant;

public enum AssistantEmotion
{
    Neutral,
    Curious,
    Happy,
    Surprised,
    Confused,
    Sad,
    Determined,
    Wink
}

public sealed class AssistantEmotionEngine
{
    public AssistantEmotion EvaluateConversation(
        string requestText,
        string replyText,
        AssistantBackend backend,
        ResponseStyle style)
    {
        string request = requestText.Trim().ToLowerInvariant();
        string reply = replyText.Trim().ToLowerInvariant();

        if (backend == AssistantBackend.Local && ContainsAny(reply,
            "ai online belum tersedia", "online ai is unavailable", "local mode is active"))
            return AssistantEmotion.Confused;

        if (ContainsAny(reply, "terjadi kesalahan", "gagal", "tidak dapat", "tidak bisa", "error", "unavailable", "cannot", "can't", "unable"))
            return AssistantEmotion.Confused;

        if (ContainsAny(request, "sedih", "kecewa", "lagi down", "sad", "upset"))
            return AssistantEmotion.Sad;

        if (ContainsAny(request, "perbaiki", "selesaikan", "debug", "lanjut", "fix", "solve", "continue"))
            return AssistantEmotion.Determined;

        if (ContainsAny(request, "terima kasih", "makasih", "thanks", "thank you"))
            return AssistantEmotion.Happy;

        if (ContainsAny(reply, "berhasil", "selesai", "bagus", "great", "success", "done"))
            return AssistantEmotion.Happy;

        if (ContainsAny(reply, "wah", "wow"))
            return AssistantEmotion.Surprised;

        if (style == ResponseStyle.Playful && ContainsAny(reply, "✨", "😉"))
            return AssistantEmotion.Wink;

        bool question = request.Contains('?') || Regex.IsMatch(request,
            @"^(apa|siapa|kenapa|mengapa|bagaimana|gimana|kapan|di mana|dimana|what|why|how|when|where|who)\b",
            RegexOptions.IgnoreCase);
        return question ? AssistantEmotion.Curious : AssistantEmotion.Neutral;
    }

    public AssistantEmotion EvaluateTool(ToolInvocation invocation, ToolExecutionResult result)
    {
        if (!result.Success)
            return AssistantEmotion.Confused;

        return invocation.Name switch
        {
            BuiltInToolNames.MemoryRemember => AssistantEmotion.Happy,
            BuiltInToolNames.MemoryForget => AssistantEmotion.Neutral,
            _ => AssistantEmotion.Neutral
        };
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
