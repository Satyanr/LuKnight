using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace LuKnight.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _art;
    private readonly Forms.ToolStripMenuItem _visibility;
    private bool _disposed;
    public Forms.ContextMenuStrip Menu { get; } = new();

    public TrayIconService(
    Action toggle,
    Action show,
    Action chat,
    Action settings,
    Action restart,
    Action exit)
    {
        _visibility =
            Add(
                "Hide Lu-Knight",
                toggle);


        Add(
            "Open Chat",
            chat);


        Add(
            "Settings",
            settings);


        Menu.Items.Add(
            new Forms.ToolStripSeparator());


        Add(
            "Restart Lu-Knight",
            restart);


        Menu.Items.Add(
            new Forms.ToolStripSeparator());


        Add(
            "Exit",
            exit);


        // =============================
        // TRAY ICON INITIALIZATION
        // =============================

        _art =
            CreateIcon();


        _icon =
            new Forms.NotifyIcon
            {
                Text =
                    "Lu-Knight",

                Icon =
                    _art,

                ContextMenuStrip =
                    Menu
            };


        _icon.MouseDoubleClick +=
            (_, e) =>
            {
                if (e.Button ==
                    Forms.MouseButtons.Left)
                {
                    show();
                }
            };
        _icon.BalloonTipClicked += (_, _) => settings();

    }
    private Forms.ToolStripMenuItem Add(string text, Action command)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => { if (!_disposed) command(); };
        Menu.Items.Add(item);
        return item;
    }

    public void Show() { ObjectDisposedException.ThrowIf(_disposed, this); _icon.Visible = true; }
    public void NotifyUpdate(string version)
    {
        if (!_disposed) _icon.ShowBalloonTip(5000, "Lu-Knight update", $"Versi {version} tersedia. Buka Settings → About untuk memperbarui.", Forms.ToolTipIcon.Info);
    }
    public void SetCharacterVisible(bool visible) => _visibility.Text = visible ? "Hide Lu-Knight" : "Show Lu-Knight";

    private static Icon CreateIcon()
    {
        string official = Path.Combine(AppContext.BaseDirectory, "Assets", "LuKnight.ico");
        if (File.Exists(official))
        {
            try { using var icon = new Icon(official, 32, 32); return (Icon)icon.Clone(); }
            catch (Exception ex) when (ex is IOException or ArgumentException) { }
        }
        try
        {
            using var source = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight/Idle/idle_000.png"));
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(2, 0, 28, 32), new Rectangle(50, 175, 415, 470), GraphicsUnit.Pixel);
            }
            nint handle = bitmap.GetHicon();
            try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        Menu.Dispose();
        _art.Dispose();
    }
}
