using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

using Forms =
    System.Windows.Forms;


namespace LuKnight;


public partial class App : Application
{
    private Forms.NotifyIcon?
        _trayIcon;


    private Forms.ContextMenuStrip?
        _trayMenu;


    private Forms.ToolStripMenuItem?
        _showHideMenuItem;


    private MainWindow?
        _mainWindow;


    private bool
        _isExiting;


    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);


        // App hanya benar-benar berhenti
        // melalui Exit / Windows shutdown.
        ShutdownMode =
            ShutdownMode.OnExplicitShutdown;


        _mainWindow =
            new MainWindow();


        MainWindow =
            _mainWindow;


        _mainWindow.Closing +=
            MainWindow_Closing;


        _mainWindow.IsVisibleChanged +=
            MainWindow_IsVisibleChanged;


        CreateTrayIcon();


        _mainWindow.Show();
    }


    private void CreateTrayIcon()
    {
        _trayMenu =
            new Forms.ContextMenuStrip();


        _showHideMenuItem =
            new Forms.ToolStripMenuItem(
                "Hide Lu-Knight");


        _showHideMenuItem.Click +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    ToggleCharacterVisibility);
            };


        var openChatItem =
            new Forms.ToolStripMenuItem(
                "Open Chat");


        openChatItem.Click +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        _mainWindow?
                            .OpenChatFromTray();
                    });
            };


        var restartItem =
            new Forms.ToolStripMenuItem(
                "Restart Lu-Knight");


        restartItem.Click +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    RestartApplication);
            };


        var exitItem =
            new Forms.ToolStripMenuItem(
                "Exit");


        exitItem.Click +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    ExitApplication);
            };


        _trayMenu.Items.Add(
            _showHideMenuItem);


        _trayMenu.Items.Add(
            openChatItem);


        _trayMenu.Items.Add(
            new Forms.ToolStripSeparator());


        _trayMenu.Items.Add(
            restartItem);


        _trayMenu.Items.Add(
            new Forms.ToolStripSeparator());


        _trayMenu.Items.Add(
            exitItem);


        _trayIcon =
            new Forms.NotifyIcon
            {
                Text =
                    "Lu-Knight",

                Icon =
                    System.Drawing
                        .SystemIcons
                        .Application,

                ContextMenuStrip =
                    _trayMenu,

                Visible =
                    true
            };


        _trayIcon.DoubleClick +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    ToggleCharacterVisibility);
            };
    }


    private void ToggleCharacterVisibility()
    {
        if (_mainWindow is null)
        {
            return;
        }


        if (_mainWindow.IsVisible)
        {
            _mainWindow.HideToTray();
        }
        else
        {
            _mainWindow.ShowFromTray();
        }


        UpdateTrayMenu();
    }


    private void UpdateTrayMenu()
    {
        if (_showHideMenuItem is null ||
            _mainWindow is null)
        {
            return;
        }


        _showHideMenuItem.Text =
            _mainWindow.IsVisible
                ? "Hide Lu-Knight"
                : "Show Lu-Knight";
    }


    private void MainWindow_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        UpdateTrayMenu();
    }


    private void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }


        // Alt+F4 sekarang berarti
        // sembunyikan ke tray,
        // bukan mematikan Lu-Knight.
        e.Cancel =
            true;


        _mainWindow?
            .HideToTray();


        UpdateTrayMenu();
    }


    private void RestartApplication()
    {
        string? executable =
            Environment.ProcessPath;


        if (string.IsNullOrWhiteSpace(
                executable))
        {
            MessageBox.Show(
                "Lu-Knight tidak dapat menemukan executable untuk restart.",
                "Lu-Knight",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        executable,

                    WorkingDirectory =
                        AppContext.BaseDirectory,

                    UseShellExecute =
                        true
                });


            ExitApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Restart gagal:\n{ex.Message}",
                "Lu-Knight",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }


        _isExiting =
            true;


        if (_trayIcon is not null)
        {
            _trayIcon.Visible =
                false;
        }


        _mainWindow?
            .Close();


        Shutdown();
    }


    protected override void OnSessionEnding(
        SessionEndingCancelEventArgs e)
    {
        // Jangan cegah Windows
        // logout / shutdown.
        _isExiting =
            true;


        base.OnSessionEnding(e);
    }


    protected override void OnExit(
        ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Closing -=
                MainWindow_Closing;


            _mainWindow.IsVisibleChanged -=
                MainWindow_IsVisibleChanged;
        }


        if (_trayIcon is not null)
        {
            _trayIcon.Visible =
                false;


            _trayIcon.Dispose();


            _trayIcon =
                null;
        }


        _trayMenu?
            .Dispose();


        _trayMenu =
            null;


        base.OnExit(e);
    }
}