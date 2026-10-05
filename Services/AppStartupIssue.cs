namespace LuKnight.Services;

public enum AppStartupModule
{
    DesktopAppIndex,
    UserSkills,
    Scheduler
}

public sealed record AppStartupIssue(AppStartupModule Module, string Message);
