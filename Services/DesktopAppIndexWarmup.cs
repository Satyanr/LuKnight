namespace LuKnight.Services;

public static class DesktopAppIndexWarmup
{
    public static void Start(IDesktopAppCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var thread = new Thread(() =>
        {
            try { catalog.Refresh(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(DiagnosticPrivacy.TraceFailure("Desktop app index warm-up", ex)); }
        }) { IsBackground = true, Name = "LuKnight App Index" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
