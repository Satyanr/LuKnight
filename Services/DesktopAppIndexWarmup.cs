namespace LuKnight.Services;

public static class DesktopAppIndexWarmup
{
    public static void Start(IDesktopAppCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var thread = new Thread(() =>
        {
            try { catalog.Refresh(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("App index warm-up failed: " + ex.Message); }
        }) { IsBackground = true, Name = "LuKnight App Index" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
