namespace LuKnight.Services;

public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ChatCoordinator Chat { get; }
    public UpdateService Updates { get; }
    public AppServices(SettingsService? settings = null, ICredentialService? credentials = null)
    {
        Settings = settings ?? new();
        Chat = new(credentials ?? new SecureCredentialService(), Settings.Current.Chat);
        Updates = new(Settings);
    }
}
