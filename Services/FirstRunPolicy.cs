using LuKnight.Models;

namespace LuKnight.Services;


public static class FirstRunPolicy
{
    public static bool ShouldShow(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);


        return
            !settings.Onboarding
                .Completed;
    }


    public static AppSettings Complete(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);


        return settings with
        {
            Onboarding =
                settings.Onboarding with
                {
                    Completed =
                        true
                }
        };
    }
}
