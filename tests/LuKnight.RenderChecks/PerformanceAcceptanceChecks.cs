using System.Diagnostics;
using LuKnight.Services;

internal static partial class Program
{
    private const int ResolverIterations = 20_000;
    private const int CatalogIterations = 5_000;

    // Generous regression/liveness limits, not product performance promises.
    private static readonly TimeSpan PureWorkloadLimit = TimeSpan.FromSeconds(10);
    private const long MaxResolverAllocationPerOperation = 128 * 1024;
    private const long MaxCatalogAllocationPerOperation = 256 * 1024;

    private sealed record PerformanceMeasurement(
        string Name, int Operations, TimeSpan Elapsed, long AllocatedBytes)
    {
        public double MicrosecondsPerOperation => Operations <= 0
            ? 0 : Elapsed.TotalMilliseconds * 1000 / Operations;
        public double BytesPerOperation => Operations <= 0
            ? 0 : AllocatedBytes / (double)Operations;
    }

    private static PerformanceMeasurement Measure(string name, int operations, Action body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);
        if (operations <= 0) throw new ArgumentOutOfRangeException(nameof(operations));

        body(); // Warm up JIT before measuring.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch timer = Stopwatch.StartNew();
        for (int i = 0; i < operations; i++) body();
        timer.Stop();
        long afterAllocation = GC.GetAllocatedBytesForCurrentThread();
        return new(name, operations, timer.Elapsed, Math.Max(0, afterAllocation - beforeAllocation));
    }

    private static void PrintMeasurement(PerformanceMeasurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name}: {measurement.Operations:N0} ops, " +
            $"{measurement.Elapsed.TotalMilliseconds:N1} ms total, " +
            $"{measurement.MicrosecondsPerOperation:N1} us/op, " +
            $"{measurement.BytesPerOperation:N0} B/op");
    }

    private static void CheckResolverPerformanceAcceptance()
    {
        var windows = new List<DesktopWindowTarget>();
        for (int i = 0; i < 32; i++)
        {
            windows.Add(new DesktopWindowTarget(
                (nint)(1000 + i), 2000 + i, i % 2 == 0 ? "chrome" : "code",
                i == 17 ? "LuKnight Performance Target" : $"Synthetic Window {i}",
                i, IsMinimized: false, IsForeground: i == 3));
        }
        Require(DesktopWindowTargetService.ResolveSnapshot(windows, "LuKnight Performance Target")
                .Match?.Handle == (nint)1017,
            "Performance resolver fixture does not resolve exact target.");

        int iteration = 0;
        PerformanceMeasurement measurement = Measure("Window resolver", ResolverIterations, () =>
        {
            string query = (iteration++ & 3) switch
            {
                0 => "LuKnight Performance Target",
                1 => "chrome synthetic",
                2 => "window aktif",
                _ => "missing synthetic window"
            };
            _ = DesktopWindowTargetService.ResolveSnapshot(windows, query);
        });
        PrintMeasurement(measurement);
        Require(measurement.Elapsed < PureWorkloadLimit,
            "Window resolver workload exceeded generous liveness limit.");
        Require(measurement.BytesPerOperation <= MaxResolverAllocationPerOperation,
            "Window resolver allocation per operation became unexpectedly large.");
        Require(DesktopWindowTargetService.ResolveSnapshot(windows, "window aktif").Match?.Handle == (nint)1003,
            "Sustained resolver workload changed foreground semantics.");
    }

    private static void CheckCatalogPerformanceAcceptance()
    {
        DateTimeOffset now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        int discoveries = 0;
        var applications = new List<DesktopAppTarget>();
        for (int i = 0; i < 64; i++)
            applications.Add(Installed($"Performance Application {i}", $"PerfApp{i}", $"perf-{i}"));
        var catalog = new DesktopAppCatalogService(
            discover: () => { discoveries++; return applications; }, clock: () => now);
        catalog.Refresh();
        Require(discoveries == 1, "Performance catalog fixture did not perform initial discovery.");
        Require(catalog.Resolve("Performance Application 42").Found,
            "Performance catalog positive-control resolution failed.");

        int iteration = 0;
        PerformanceMeasurement measurement = Measure("Application resolver", CatalogIterations, () =>
        {
            int value = iteration++ % 64;
            _ = catalog.Resolve($"perf-{value}");
        });
        PrintMeasurement(measurement);
        Require(measurement.Elapsed < PureWorkloadLimit,
            "Application resolver workload exceeded generous liveness limit.");
        Require(measurement.BytesPerOperation <= MaxCatalogAllocationPerOperation,
            "Application resolver allocation per operation became unexpectedly large.");
        Require(discoveries == 1, "Repeated application resolution bypassed catalog refresh cache.");
        for (int i = 0; i < 100; i++)
            _ = catalog.Resolve("definitely-missing-performance-app");
        Require(discoveries == 1, "Repeated application misses scanned discovery sources too frequently.");
        now = now.AddSeconds(31);
        _ = catalog.Resolve("definitely-missing-performance-app");
        Require(discoveries == 2, "Catalog miss failed to refresh after existing refresh interval.");
    }

    private static void CheckAwarenessFormattingAcceptance()
    {
        DesktopApplicationContext[] applications = Enumerable.Range(0, 8)
            .Select(index => new DesktopApplicationContext(
                (nint)(index + 1), index + 10, $"process-{index}",
                index % 2 == 0 ? DesktopApplicationKind.Browser : DesktopApplicationKind.CodeEditor))
            .ToArray();
        PerformanceMeasurement measurement = Measure("Awareness formatting", 50_000, () =>
        {
            string[] formatted = DesktopApplicationAwarenessService.FormatApplications(applications);
            if (formatted.Length != applications.Length)
                throw new InvalidOperationException("Formatting length changed.");
        });
        PrintMeasurement(measurement);
        Require(measurement.Elapsed < PureWorkloadLimit,
            "Application awareness formatting exceeded generous liveness limit.");
    }

    private static void ReportNativePerformanceDiagnostics()
    {
        Console.WriteLine();
        Console.WriteLine("Native diagnostics (informational only):");
        try
        {
            Stopwatch timer = Stopwatch.StartNew();
            IReadOnlyList<DesktopWindowInfo> windows = DesktopWindowService.GetVisibleWindows();
            timer.Stop();
            Console.WriteLine($"Visible-window snapshot: {windows.Count} windows, {timer.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Visible-window snapshot: unavailable ({DiagnosticPrivacy.ExceptionTag(ex)})");
        }
        try
        {
            Stopwatch timer = Stopwatch.StartNew();
            DesktopApplicationSnapshot awareness = DesktopApplicationAwarenessService.Capture();
            timer.Stop();
            // Counts and timing only; never print desktop metadata.
            Console.WriteLine($"Application awareness: {awareness.Applications.Count} categories/apps, {timer.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application awareness: unavailable ({DiagnosticPrivacy.ExceptionTag(ex)})");
        }
        try
        {
            var catalog = new DesktopAppCatalogService();
            Stopwatch timer = Stopwatch.StartNew();
            catalog.Refresh();
            timer.Stop();
            Console.WriteLine($"Application discovery: {catalog.Applications.Count} targets, " +
                $"{timer.ElapsedMilliseconds} ms, healthy={catalog.LastRefreshSucceeded}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application discovery: unavailable ({DiagnosticPrivacy.ExceptionTag(ex)})");
        }
        Console.WriteLine("Native diagnostic timing is not a PASS/FAIL threshold.");
    }

    private static void CheckPerformanceAcceptance()
    {
        CheckResolverPerformanceAcceptance();
        CheckCatalogPerformanceAcceptance();
        CheckAwarenessFormattingAcceptance();
        ReportNativePerformanceDiagnostics();
        Console.WriteLine();
        Console.WriteLine("Performance acceptance uses generous regression bounds; " +
            "reported native timings are informational, not hardware SLAs.");
    }
}
