using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LuKnight.Services;

public sealed record SystemContextSnapshot(
    string OperatingSystem,
    string OsArchitecture,
    string ProcessArchitecture,
    int LogicalProcessorCount,
    ulong? TotalPhysicalMemoryBytes,
    ulong? AvailablePhysicalMemoryBytes,
    int? MemoryLoadPercent,
    bool NetworkInterfaceAvailable,
    bool? AcPowerConnected,
    bool? BatteryPresent,
    int? BatteryPercent,
    TimeSpan Uptime);

public static class SystemContextService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static SystemContextSnapshot Capture()
    {
        ulong? totalMemory = null;
        ulong? availableMemory = null;
        int? memoryLoad = null;
        var memory = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (GlobalMemoryStatusEx(ref memory))
        {
            totalMemory = memory.TotalPhysical;
            availableMemory = memory.AvailablePhysical;
            memoryLoad = (int)memory.MemoryLoad;
        }

        bool? acPower = null;
        bool? batteryPresent = null;
        int? batteryPercent = null;
        if (GetSystemPowerStatus(out SystemPowerStatus power))
        {
            acPower = power.AcLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null
            };

            if (power.BatteryFlag != 255)
                batteryPresent = (power.BatteryFlag & 128) == 0;

            if (batteryPresent == true && power.BatteryLifePercent <= 100)
                batteryPercent = power.BatteryLifePercent;
        }

        bool networkAvailable;
        try
        {
            networkAvailable = NetworkInterface.GetIsNetworkAvailable();
        }
        catch (NetworkInformationException)
        {
            networkAvailable = false;
        }

        return new SystemContextSnapshot(
            OperatingSystem: RuntimeInformation.OSDescription,
            OsArchitecture: RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessorCount: Environment.ProcessorCount,
            TotalPhysicalMemoryBytes: totalMemory,
            AvailablePhysicalMemoryBytes: availableMemory,
            MemoryLoadPercent: memoryLoad,
            NetworkInterfaceAvailable: networkAvailable,
            AcPowerConnected: acPower,
            BatteryPresent: batteryPresent,
            BatteryPercent: batteryPercent,
            Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
}
