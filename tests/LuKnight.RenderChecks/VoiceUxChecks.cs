using LuKnight.Models;
using LuKnight.Views;

internal static partial class Program
{
    private static void CheckVoiceUx()
    {
        var panel = new ChatPanel();

        Require(
            panel.CurrentVoiceState == VoiceInteractionState.Idle,
            "Voice UI did not start idle.");
        Require(
            panel.VoicePrivacySummary.Contains("Mic off", StringComparison.Ordinal),
            "Idle voice privacy indicator is incorrect.");

        panel.SetVoiceState(VoiceInteractionState.Listening);
        Require(
            panel.CurrentVoiceState == VoiceInteractionState.Listening &&
            panel.VoicePrivacySummary.Contains("Mic active", StringComparison.Ordinal),
            "Listening state does not expose active microphone.");

        panel.SetVoiceState(VoiceInteractionState.Transcribing);
        Require(
            panel.VoicePrivacySummary.Contains("Mic off", StringComparison.Ordinal),
            "Transcribing UI incorrectly reports microphone active.");

        panel.SetVoiceState(VoiceInteractionState.Thinking);
        Require(
            panel.VoicePrivacySummary.Contains("Mic off", StringComparison.Ordinal),
            "Thinking UI incorrectly reports microphone active.");

        panel.SetVoiceState(VoiceInteractionState.Speaking);
        Require(
            panel.VoicePrivacySummary.Contains("Mic off", StringComparison.Ordinal),
            "Speaking UI incorrectly reports microphone active.");

        panel.SetDraftMessage("buka notepad");
        Require(panel.HasDraftMessage, "Voice review draft was not recognized.");

        ChatSettings defaults = new();
        Require(
            defaults.VoiceSubmissionMode == VoiceSubmissionMode.SendImmediately,
            "Voice submission default changed unexpectedly.");
    }
}
