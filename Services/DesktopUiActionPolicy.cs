namespace LuKnight.Services;

public static class DesktopUiActionPolicy
{
    public static bool IsTemporarilyBlocked(
        DesktopUiNodeSnapshot node,
        out string reason) =>
        IsTemporarilyBlocked(
            null,
            node,
            out reason);

    public static bool IsTemporarilyBlocked(
        DesktopWindowTarget? window,
        DesktopUiNodeSnapshot node,
        out string reason)
    {
        DesktopUiActionRiskAssessment assessment =
            DesktopUiActionRiskClassifier
                .ClassifyButton(
                    node,
                    window);

        if (!assessment.Valid)
        {
            reason =
                assessment.Message;

            return true;
        }

        if (assessment.Risk ==
            Assistant.AssistantActionRisk.Prohibited)
        {
            reason =
                assessment.Message;

            return true;
        }

        if (assessment.Risk ==
            Assistant.AssistantActionRisk.Sensitive)
        {
            reason =
                "Tindakan ini sudah diklasifikasikan Sensitive, tetapi masih diblokir sampai stronger confirmation tersedia.";

            return true;
        }

        reason =
            string.Empty;

        return false;
    }
}
