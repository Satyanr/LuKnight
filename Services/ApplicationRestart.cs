using System.Diagnostics;
using System.IO;

namespace LuKnight.Services;

public static class ApplicationRestart
{
    public static ProcessStartInfo CreateStartInfo(string executable, string assembly, IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Executable Lu-Knight tidak ditemukan.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(assembly);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
