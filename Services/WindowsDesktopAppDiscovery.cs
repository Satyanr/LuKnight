using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LuKnight.Services;

public static class WindowsDesktopAppDiscovery
{
    public static IEnumerable<DesktopAppTarget> Discover()
    {
        var apps = BuiltIns().ToList();
        object? shell = null;
        try
        {
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is not null) shell = Activator.CreateInstance(type);
        }
        catch (COMException) { }
        try
        {
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 16 };
                    foreach (string link in Directory.EnumerateFiles(root, "*.lnk", options))
                        if (shell is not null && ReadShortcut(link, shell) is { } app) apps.Add(app);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
            }
        }
        finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? paths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (paths is null) continue;
                foreach (string name in paths.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? key = paths.OpenSubKey(name);
                        string? path = (key?.GetValue(null) as string)?.Trim().Trim('"');
                        if (path is null) continue;
                        path = Environment.ExpandEnvironmentVariables(path);
                        if (!DesktopAppPolicy.IsLocalExecutable(path) || !File.Exists(path)) continue;
                        var app = FromAppPath(name, path);
                        if (DesktopAppPolicy.IsAllowed(app) && HasAllowedExecutableIdentity(path)) apps.Add(app);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or PlatformNotSupportedException) { }
        }
        apps.AddRange(DiscoverAppsFolder());
        return apps;
    }

    private static IEnumerable<DesktopAppTarget> DiscoverAppsFolder()
    {
        var result = new List<DesktopAppTarget>();
        object? shell = null, folder = null, items = null;
        try
        {
            Type? type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return result;
            shell = Activator.CreateInstance(type);
            if (shell is null) return result;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null) return result;
            items = ((dynamic)folder).Items();
            int count = (int)((dynamic)items).Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    if (item is null) continue;
                    string name = Convert.ToString(((dynamic)item).Name)?.Trim() ?? "";
                    string aumid = Convert.ToString(((dynamic)item).ExtendedProperty("System.AppUserModel.ID"))?.Trim() ?? "";
                    if (name.Length == 0 || !DesktopAppPolicy.IsValidAppUserModelId(aumid)) continue;
                    var app = new DesktopAppTarget(DesktopAppCatalogService.CreateId("appsfolder|" + aumid),
                        name, "shell:AppsFolder\\" + aumid, [],
                        DesktopNameNormalizer.BuildAliases(name, []).ToArray(), DesktopAppSource.AppsFolder)
                    { AppUserModelId = aumid, RegistrationIdentity = "aumid|" + aumid };
                    if (DesktopAppPolicy.IsAllowed(app)) result.Add(app);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException
                    or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { ReleaseCom(item); }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally { ReleaseCom(items); ReleaseCom(folder); ReleaseCom(shell); }
        return result;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    // Reads existing .lnk metadata only. Never calls Run, Exec, Save or a script interpreter.
    public static DesktopAppTarget? ReadShortcut(string path) => ReadShortcut(path, null);

    private static DesktopAppTarget? ReadShortcut(string path, object? existingShell)
    {
        object? shell = null, shortcut = null;
        try
        {
            if (!File.Exists(path) || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            shell = existingShell;
            if (shell is null)
            {
                Type? type = Type.GetTypeFromProgID("WScript.Shell");
                if (type is null) return null;
                shell = Activator.CreateInstance(type);
            }
            if (shell is null) return null;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string target = Environment.ExpandEnvironmentVariables(((string)((dynamic)shortcut).TargetPath).Trim());
            string arguments = (string)((dynamic)shortcut).Arguments;
            string workingDirectory = Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut).WorkingDirectory);
            if (!DesktopAppPolicy.IsLocalExecutable(target) || !File.Exists(target)) return null;
            string display = Path.GetFileNameWithoutExtension(path);
            string process = Path.GetFileNameWithoutExtension(target);
            string? registrationIdentity = SquirrelIdentity(target, arguments, out string? startedProcess);
            string[] processes = [startedProcess ?? process];
            var app = new DesktopAppTarget(DesktopAppCatalogService.CreateId("startmenu|" + path), display,
                path, processes, DesktopNameNormalizer.BuildAliases(display, processes).ToArray(), DesktopAppSource.StartMenu)
            { ResolvedExecutable = target, Arguments = arguments, WorkingDirectory = workingDirectory, RegistrationIdentity = registrationIdentity };
            return DesktopAppPolicy.IsAllowed(app) && HasAllowedExecutableIdentity(target) ? app : null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or SecurityException) { return null; }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (existingShell is null && shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    public static bool IsLaunchUnchanged(DesktopAppTarget app)
    {
        if (!DesktopAppPolicy.IsAllowed(app)) return false;
        if (app.Source == DesktopAppSource.StartMenu)
            return ReadShortcut(app.LaunchTarget)?.Fingerprint == app.Fingerprint;
        if (app.Source == DesktopAppSource.AppPaths)
            return File.Exists(app.LaunchTarget) && HasAllowedExecutableIdentity(app.LaunchTarget);
        return true;
    }

    private static bool HasAllowedExecutableIdentity(string path)
    {
        try
        {
            string? originalName = FileVersionInfo.GetVersionInfo(path).OriginalFilename;
            return string.IsNullOrWhiteSpace(originalName) || !DesktopAppPolicy.IsRestrictedExecutable(originalName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static DesktopAppTarget FromAppPath(string registeredName, string target)
    {
        string process = Path.GetFileNameWithoutExtension(target);
        // The registration can name the real app while its target is a packaged activation helper.
        string display = Path.GetFileNameWithoutExtension(registeredName);
        string[] processes = new[] { process, display }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(DesktopAppCatalogService.CreateId("apppath|" + target), display, target, processes,
            DesktopNameNormalizer.BuildAliases(display, processes).ToArray(), DesktopAppSource.AppPaths);
    }

    public static string? SquirrelIdentity(string executable, string arguments, out string? startedProcess)
    {
        startedProcess = null;
        string? directory = Path.GetDirectoryName(executable);
        if (directory is null) return null;
        if (Path.GetFileName(executable).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            Match match = Regex.Match(arguments, "^--processStart\\s+\"?([\\w. -]+\\.exe)\"?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            startedProcess = Path.GetFileNameWithoutExtension(match.Groups[1].Value);
            return Path.Combine(directory, match.Groups[1].Value);
        }
        if (arguments.Length == 0 && Regex.IsMatch(Path.GetFileName(directory), @"^app-\d+(\.\d+)+$", RegexOptions.IgnoreCase))
        {
            string? parent = Path.GetDirectoryName(directory);
            if (parent is not null && File.Exists(Path.Combine(parent, "Update.exe")))
                return Path.Combine(parent, Path.GetFileName(executable));
        }
        return null;
    }

    public static IReadOnlyList<DesktopAppTarget> BuiltIns()
    {
        DesktopAppTarget App(string id, string display, string target, string[] processes, params string[] aliases) =>
            new(id, display, target, processes, aliases.Concat(DesktopNameNormalizer.BuildAliases(display, processes)).Distinct().ToArray(), DesktopAppSource.BuiltIn);
        return new[]
        {
            App("notepad", "Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe"), ["notepad"], "notes"),
            App("calculator", "Calculator", Path.Combine(Environment.SystemDirectory, "calc.exe"), ["CalculatorApp", "Calculator"], "kalkulator"),
            App("explorer", "File Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), ["explorer"], "windows explorer"),
            App("settings", "Windows Settings", "ms-settings:", ["SystemSettings"], "settings", "pengaturan"),
            App("chrome", "Google Chrome", "chrome.exe", ["chrome"]),
            App("edge", "Microsoft Edge", "msedge.exe", ["msedge"], "edge"),
            App("firefox", "Mozilla Firefox", "firefox.exe", ["firefox"]),
            App("vscode", "Visual Studio Code", "Code.exe", ["code"], "vscode", "vs code"),
            App("excel", "Microsoft Excel", "excel.exe", ["excel"]),
            App("word", "Microsoft Word", "winword.exe", ["winword"], "word"),
            App("powerpoint", "Microsoft PowerPoint", "powerpnt.exe", ["powerpnt"], "powerpoint")
        };
    }
}
