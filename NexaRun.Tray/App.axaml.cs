using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Tray.Helpers;
using NexaRun.Tray.Services;
using NexaRun.Tray.Views;

namespace NexaRun.Tray;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private DashboardWindow? _dashboardWindow;
    private readonly IpcClient _ipc = new();
    private TrayConfigService? _config;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        NexaRunPaths.EnsureDirectories();

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            TrayCrashReporter.Report("UIThread", e.Exception, isTerminating: true);
        };

        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _config = new TrayConfigService(_ipc);

        try
        {
            if (WindowsShellHelper.IsNotificationAreaAvailable())
                BuildTray(desktop);
            else
                StartWindowOnlyMode(desktop, "Windows shell is not available (no Explorer). Running without tray icon.");
        }
        catch (Exception ex)
        {
            TrayCrashReporter.Report("BuildTray", ex, isTerminating: false);
            StartWindowOnlyMode(desktop, $"Could not create tray icon: {ex.Message}");
        }
    }

    private void BuildTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        AddMenuItem(menu, "Processes", (_, _) => ShowMainWindow());
        AddMenuItem(menu, "Dashboard", (_, _) => ShowDashboard());
        AddMenuItem(menu, "Add Process", (_, _) => ShowAddProcess());
        AddMenuItem(menu, "Import JSON...", async (_, _) => await ImportJson());
        AddMenuItem(menu, "Import from PM2...", async (_, _) => await ImportPm2());
        AddMenuItem(menu, "Export JSON...", async (_, _) => await ExportJson());
        AddMenuItem(menu, "Settings...", (_, _) => ShowSettings());

        menu.Items.Add(new NativeMenuItemSeparator());
        AddMenuItem(menu, "Open data folder", (_, _) => OpenDataFolder());
        AddMenuItem(menu, "Exit NexaRun", (_, _) => desktop.Shutdown());

        _trayIcon = new TrayIcon
        {
            Icon = TrayIconHelper.LoadIcon(),
            ToolTipText = "NexaRun — right-click for menu",
            IsVisible = true,
            Menu = menu
        };

        _trayIcon.Clicked += (_, _) => ShowDashboard();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void StartWindowOnlyMode(IClassicDesktopStyleApplicationLifetime desktop, string reason)
    {
        TrayCrashReporter.Report("WindowOnlyMode", new InvalidOperationException(reason), isTerminating: false);
        ShowDashboard();
        desktop.Exit += (_, _) =>
        {
            _dashboardWindow?.Close();
            _mainWindow?.Close();
        };
    }

    private static void AddMenuItem(NativeMenu menu, string header, EventHandler click)
    {
        var item = new NativeMenuItem(header);
        item.Click += click;
        menu.Items.Add(item);
    }

    private async Task ImportJson()
    {
        if (_config == null) return;
        await TrayImportExportActions.ImportFromPicker(VisibleOwner(), _config, RefreshMainWindow);
    }

    private async Task ImportPm2()
    {
        if (_config == null) return;
        await TrayImportExportActions.ImportFromPm2(VisibleOwner(), _config, RefreshMainWindow);
    }

    private async Task ExportJson()
    {
        if (_config == null) return;
        await TrayImportExportActions.ExportToPicker(VisibleOwner(), _config);
    }

    private static void OpenDataFolder()
    {
        NexaRunPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = NexaRunPaths.DataDir,
            UseShellExecute = true
        });
    }

    private Task RefreshMainWindow() =>
        _mainWindow?.IsVisible == true ? _mainWindow.RefreshFromTray() : Task.CompletedTask;

    private void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsVisible)
        {
            _mainWindow = new MainWindow(_ipc);
            _mainWindow.Show();
        }
        else
        {
            _mainWindow.Activate();
        }
    }

    private void ShowDashboard()
    {
        if (_dashboardWindow == null || !_dashboardWindow.IsVisible)
        {
            _dashboardWindow = new DashboardWindow(_ipc);
            _dashboardWindow.Show();
        }
        else
        {
            _dashboardWindow.Activate();
        }
    }

    private async void ShowAddProcess()
    {
        var dlg = new AddProcessWindow(_ipc);
        var owner = VisibleOwner();
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();

        await RefreshMainWindow();
    }

    private async void ShowSettings()
    {
        var dlg = new SettingsWindow(_ipc);
        var owner = VisibleOwner();
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    private Window? VisibleOwner()
    {
        if (_mainWindow?.IsVisible == true) return _mainWindow;
        if (_dashboardWindow?.IsVisible == true) return _dashboardWindow;
        return null;
    }
}
