using LuKnight.Models;

namespace LuKnight.Assistant;

public sealed record AssistantActionPermissionDecision(
    bool Allowed,
    string Message);

public static class AssistantActionPermissionPolicy
{
    public static AssistantActionPermissionDecision
        Evaluate(
            PreparedAssistantAction action,
            DesktopPermissionLevel permission)
    {
        ArgumentNullException.ThrowIfNull(
            action);

        if (!Enum.IsDefined(
                permission))
        {
            return Block(
                "Permission desktop tidak valid. Tindakan dibatalkan.");
        }

        if (!Enum.IsDefined(
                action.Risk))
        {
            return Block(
                "Klasifikasi risiko tindakan tidak valid. Tindakan dibatalkan.");
        }

        if (action.Risk ==
            AssistantActionRisk.Prohibited)
        {
            return Block(
                "Tindakan ini tidak diizinkan oleh kebijakan keamanan Lu-Knight.");
        }

        bool allowed =
            permission switch
            {
                DesktopPermissionLevel.ObserveOnly =>
                    false,

                DesktopPermissionLevel.Navigation =>
                    action.Risk ==
                    AssistantActionRisk.Navigation,

                DesktopPermissionLevel.Interaction =>
                    action.Risk is
                        AssistantActionRisk.Navigation
                        or AssistantActionRisk.Interaction,

                DesktopPermissionLevel.Sensitive =>
                    action.Risk is
                        AssistantActionRisk.Navigation
                        or AssistantActionRisk.Interaction
                        or AssistantActionRisk.Sensitive,

                _ =>
                    false
            };

        if (allowed)
        {
            return new(
                true,
                string.Empty);
        }

        string message =
            action.Risk switch
            {
                AssistantActionRisk.Navigation =>
                    "Tindakan ini memerlukan desktop permission Navigation atau lebih tinggi.",

                AssistantActionRisk.Interaction =>
                    "Tindakan ini memerlukan desktop permission Interaction atau lebih tinggi.",

                AssistantActionRisk.Sensitive =>
                    "Tindakan sensitif ini memerlukan desktop permission Sensitive.",

                _ =>
                    "Tindakan ini tidak diizinkan."
            };

        return Block(
            message);
    }

    private static AssistantActionPermissionDecision
        Block(
            string message) =>
        new(
            false,
            message);
}

public sealed record AssistantActionConfirmationDecision(
    bool Allowed,
    string Message);

public static class AssistantActionConfirmationPolicy
{
    public static AssistantActionConfirmationDecision
        Evaluate(
            PreparedAssistantAction action)
    {
        ArgumentNullException.ThrowIfNull(
            action);

        if (!Enum.IsDefined(
                action.Confirmation))
        {
            return Block(
                "Status konfirmasi tindakan tidak valid.");
        }

        if (!Enum.IsDefined(
                action.Risk))
        {
            return Block(
                "Klasifikasi risiko tindakan tidak valid.");
        }

        bool allowed =
            action.Risk switch
            {
                AssistantActionRisk.Navigation =>
                    action.Confirmation is
                        AssistantActionConfirmation.Standard
                        or AssistantActionConfirmation.Strong,

                AssistantActionRisk.Interaction =>
                    action.Confirmation is
                        AssistantActionConfirmation.Standard
                        or AssistantActionConfirmation.Strong,

                AssistantActionRisk.Sensitive =>
                    action.Confirmation ==
                    AssistantActionConfirmation.Strong,

                AssistantActionRisk.Prohibited =>
                    false,

                _ =>
                    false
            };

        if (allowed)
        {
            return new(
                true,
                string.Empty);
        }

        string message =
            action.Risk ==
                AssistantActionRisk.Sensitive
                ? "Tindakan sensitif memerlukan konfirmasi kuat dua tahap."
                : "Tindakan belum dikonfirmasi.";

        return Block(
            message);
    }

    private static
        AssistantActionConfirmationDecision
        Block(
            string message) =>
        new(
            false,
            message);
}
