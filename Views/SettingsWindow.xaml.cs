using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;
using LuKnight.ViewModels;

namespace LuKnight.Views;

public partial class SettingsWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    public SettingsViewModel Model { get; }

    public SettingsWindow(MainWindow character) : this(new SettingsViewModel(character.GetSettingsRuntime,
        value => character.Topmost = value,
        value => { if (value) character.ShowFromTray(); else character.HideToTray(); },
        character.ResetCharacterPosition)) { }

    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = model;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        IsVisibleChanged += Settings_IsVisibleChanged;
        Model.PropertyChanged += Model_PropertyChanged;
    }

    private void Settings_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) { Model.Refresh(); _refreshTimer.Start(); }
        else _refreshTimer.Stop();
    }
    private void RefreshTimer_Tick(object? sender, EventArgs e) => Model.Refresh();
    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection)) PageScroll.ScrollToTop();
    }
    private void Repository_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Model.RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show(this, "Browser tidak dapat dibuka. Repository: " + Model.RepositoryUrl, "Lu-Knight", MessageBoxButton.OK, MessageBoxImage.Information); }
        e.Handled = true;
    }
    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        IsVisibleChanged -= Settings_IsVisibleChanged;
        Model.PropertyChanged -= Model_PropertyChanged;
        base.OnClosed(e);
    }
}
