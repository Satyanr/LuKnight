using System.IO;
using System.Runtime.InteropServices;

namespace LuKnight.Services;

public sealed record ExplorerLocationTarget(string Id, string DisplayName, string Path, IReadOnlyList<string> Aliases);

public static class ExplorerLocationCatalog
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    private static string DownloadsPath()
    {
        nint path = nint.Zero;
        try
        {
            Guid id = new("374DE290-123F-4565-9164-39C4925E467B");
            return SHGetKnownFolderPath(in id, 0, nint.Zero, out path) == 0
                ? Marshal.PtrToStringUni(path) ?? "" : "";
        }
        finally { if (path != nint.Zero) Marshal.FreeCoTaskMem(path); }
    }

    public static IReadOnlyList<ExplorerLocationTarget> Locations =>
    [
        new("home", "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ["home", "user", "folder user"]),
        new("desktop", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ["desktop", "desktop saya"]),
        new("documents", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ["documents", "document", "dokumen", "dokumen saya"]),
        new("downloads", "Downloads", DownloadsPath(), ["download", "downloads", "folder download"]),
        new("pictures", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ["pictures", "picture", "gambar", "foto"]),
        new("music", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ["music", "musik"]),
        new("videos", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), ["video", "videos"])
    ];

    // Recognition is independent of folder existence. Missing known folders must stay local too.
    public static bool TryResolve(string value, out ExplorerLocationTarget target)
    {
        string normalized = DesktopNameNormalizer.Normalize(value);
        target = Locations.FirstOrDefault(l => l.Aliases.Any(a => DesktopNameNormalizer.Normalize(a) == normalized))!;
        return target is not null;
    }

    public static bool TryResolveById(string id, out ExplorerLocationTarget target)
    {
        target = Locations.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase))!;
        return target is not null;
    }
}
