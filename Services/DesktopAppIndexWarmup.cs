namespace LuKnight.Services;

public static class DesktopAppIndexWarmup
{
    public static void Start(IDesktopAppCatalog catalog, Action? degraded = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        void ReportFailure(Exception? exception = null)
        {
            try { degraded?.Invoke(); }
            catch { /* An optional diagnostic callback cannot terminate this worker. */ }
            if (exception is not null)
                System.Diagnostics.Debug.WriteLine(DiagnosticPrivacy.TraceFailure("Desktop app index warm-up", exception));
        }

        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    catalog.Refresh();
                    if (catalog is IDesktopAppCatalogHealth { LastRefreshSucceeded: false }) ReportFailure();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Extension/test catalogs may lack their own recovery boundary.
                    ReportFailure(ex);
                }
            }) { IsBackground = true, Name = "LuKnight App Index" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportFailure(ex);
        }
    }
}
