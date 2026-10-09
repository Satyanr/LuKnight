using LuKnight.Models;

namespace LuKnight.Services;


public static class SettingsCapabilityPolicy
{
    public static AppSettings
        SetApplicationAwareness(
            AppSettings settings,
            bool enabled)
    {
        ArgumentNullException.ThrowIfNull(
            settings);


        ChatSettings chat =
            settings.Chat with
            {
                UseApplicationContext =
                    enabled
            };


        CompanionSettings companion =
            enabled
                ? settings.Companion
                : settings.Companion with
                {
                    //
                    // Do not leave proactive help armed behind
                    // a disabled prerequisite.
                    //
                    // Re-enabling application awareness must not
                    // silently re-enable proactive suggestions.
                    //

                    Enabled =
                        false
                };


        return settings with
        {
            Chat =
                chat,

            Companion =
                companion
        };
    }
}
