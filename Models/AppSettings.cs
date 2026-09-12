using LuKnight.Behaviors;

namespace LuKnight.Models;

public enum ChatProvider { Gemini, Local }
public enum ChatLanguage { Automatic, Indonesia, English }
public enum ResponseLength { Short, Normal, Detailed }
public enum ResponseStyle { Friendly, Professional, Playful }
public sealed record GeneralSettings(bool StartHidden = false, bool AlwaysOnTop = true);
public sealed record ChatSettings
{
    public ChatProvider Provider { get; init; } = ChatProvider.Gemini;
    public string Model { get; init; } = "gemini-3.8-flash";
    public ChatLanguage Language { get; init; } = ChatLanguage.Automatic;
    public ResponseLength ResponseLength { get; init; } = ResponseLength.Normal;
    public ResponseStyle Style { get; init; } = ResponseStyle.Friendly;
    public bool RememberConversation { get; init; } = true;
    public bool UseLongTermMemory { get; init; } = true;
    public bool UseApplicationContext { get; init; } = true;
}
public sealed record WindowPlacement(double Left, double Top, double Width, double Height);
public sealed record MascotPlacement(double Left, double MonitorLeft, double MonitorTop);
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public GeneralSettings General { get; init; } = new();
    public BehaviorOptions Behavior { get; init; } = new();
    public ChatSettings Chat { get; init; } = new();
    public WindowPlacement? SettingsWindow { get; init; }
    public MascotPlacement? Mascot { get; init; }
    public DateTimeOffset? LastUpdateCheck { get; init; }
}
