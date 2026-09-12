using System.Globalization;
using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class SystemStatusContextSource : IAssistantContextSource
{
    private readonly Func<bool> _enabled;
    private readonly Func<SystemContextSnapshot> _capture;

    public string Name => BuiltInContextNames.SystemStatus;

    public SystemStatusContextSource(
        Func<bool> enabled,
        Func<SystemContextSnapshot>? capture = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _capture = capture ?? SystemContextService.Capture;
    }

    public Task<ContextCaptureResult> CaptureAsync(
        ContextInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "System context sedang nonaktif. Aktifkan melalui Settings → AI & Chat."));
        }

        if (!invocation.Arguments.TryGetValue("scope", out string? rawScope) ||
            !Enum.TryParse(rawScope, true, out SystemContextScope scope) || !Enum.IsDefined(scope))
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "Scope system context tidak valid."));
        }

        string content = Format(_capture(), scope);
        return Task.FromResult(new ContextCaptureResult(
            true,
            "System context dimuat.",
            new ChatReferenceBlock("system-status", scope.ToString(), content)));
    }

    private static string Format(SystemContextSnapshot value, SystemContextScope scope) => scope switch
    {
        SystemContextScope.Battery => FormatBattery(value),
        SystemContextScope.Memory => FormatMemory(value),
        SystemContextScope.Network => FormatNetwork(value),
        SystemContextScope.OperatingSystem => FormatOperatingSystem(value),
        _ => string.Join(Environment.NewLine,
            FormatOperatingSystem(value),
            FormatMemory(value),
            FormatBattery(value),
            FormatNetwork(value),
            $"System uptime: {FormatUptime(value.Uptime)}")
    };

    private static string FormatOperatingSystem(SystemContextSnapshot value) => $"""
        Operating system: {value.OperatingSystem}
        OS architecture: {value.OsArchitecture}
        Lu-Knight process architecture: {value.ProcessArchitecture}
        Logical processor count: {value.LogicalProcessorCount}
        """;

    private static string FormatMemory(SystemContextSnapshot value) => $"""
        Physical memory total: {FormatBytes(value.TotalPhysicalMemoryBytes)}
        Physical memory available: {FormatBytes(value.AvailablePhysicalMemoryBytes)}
        Memory load: {FormatPercent(value.MemoryLoadPercent)}
        """;

    private static string FormatBattery(SystemContextSnapshot value) => $"""
        Battery present: {FormatBool(value.BatteryPresent)}
        Battery level: {FormatPercent(value.BatteryPercent)}
        AC power connected: {FormatBool(value.AcPowerConnected)}
        """;

    private static string FormatNetwork(SystemContextSnapshot value) =>
        "Network interface available: " + (value.NetworkInterfaceAvailable ? "Yes" : "No") +
        Environment.NewLine + "This does not confirm Internet connectivity.";

    private static string FormatBytes(ulong? value)
    {
        if (value is null)
            return "Unknown";

        double gib = value.Value / 1024d / 1024d / 1024d;
        return gib.ToString("0.00", CultureInfo.InvariantCulture) + " GiB";
    }

    private static string FormatPercent(int? value) => value is null ? "Unknown" : $"{value}%";

    private static string FormatBool(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown"
    };

    private static string FormatUptime(TimeSpan uptime) =>
        $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
}
