using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;
using LuKnight.ViewModels;
using LuKnight.Services;
using LuKnight.Models;

namespace LuKnight.Views;

public partial class SettingsWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    public SettingsViewModel Model { get; }
    private SettingsService? _settings;

    public SettingsWindow(MainWindow character, StartupService? startup = null) : this(new SettingsViewModel(character.GetSettingsRuntime,
        character.SetAlwaysOnTop,
        value => { if (value) character.ShowFromTray(); else character.HideToTray(); },
        character.ResetCharacterPosition, startup ?? new StartupService(), character.BehaviorSettings))
    {
        _settings = character.Services.Settings;
        Model.Product = new ProductSettingsViewModel(character.Services, character.ClearConversation, () => character.CanInstallUpdate,
            character.SaveSession, () => { if (Application.Current is App app) app.ExitForUpdate(); });
        DataContext = null; DataContext = Model;
        if (_settings.Current.SettingsWindow is { } placement)
        {
            Width = placement.Width; Height = placement.Height;
            Left = placement.Left; Top = placement.Top;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += RestoreVisiblePlacement;
        }
    }
    private void RestoreVisiblePlacement(object sender, RoutedEventArgs e)
    {
        Loaded -= RestoreVisiblePlacement;
        var bounds = DesktopMonitorService.GetWindowBounds(this);
        bool reachable = DesktopMonitorService.GetAllMonitors().Any(m =>
        {
            var intersection = Rect.Intersect(bounds, m.WorkArea);
            return !intersection.IsEmpty && intersection.Width >= 100 && intersection.Height >= 80;
        });
        if (!reachable)
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(Width, area.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, area.Height));
            Left = area.Left; Top = area.Top;
        }
    }

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
    private void UpdateKey_Click(object sender, RoutedEventArgs e)
    {
        Model.Product?.UpdateKey(ApiKeyInput.Password);
        ApiKeyInput.Clear();
    }
    private void ClearLongTermMemory_Click(object sender, RoutedEventArgs e)
    {
        ProductSettingsViewModel? product = Model.Product;
        if (product is null || !product.CanClearLongTermMemory) return;

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Hapus semua {product.LongTermMemoryCount} long-term memory Lu-Knight?\n\nPercakapan saat ini tidak akan dihapus.",
            "Clear Long-Term Memory",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            product.ClearLongTermMemory();
    }
    public void SavePlacement()
    {
        if (_settings is null || !IsLoaded) return;
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (!bounds.IsEmpty && double.IsFinite(bounds.Left))
            _settings.Update(_settings.Current with { SettingsWindow = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height) });
    }
    protected override void OnClosed(EventArgs e)
    {
        ApiKeyInput.Clear(); Model.Product?.Dispose();
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        IsVisibleChanged -= Settings_IsVisibleChanged;
        Model.PropertyChanged -= Model_PropertyChanged;
        base.OnClosed(e);
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        base.OnClosing(e);
    }
}
