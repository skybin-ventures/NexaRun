using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using NexaRun.Tray.Services;
using NexaRun.Tray.ViewModels;

namespace NexaRun.Tray.Views;

public partial class MainWindow : Window
{
    private IpcClient _ipc = null!;
    private TrayConfigService _config = null!;
    private DispatcherTimer? _timer;
    private ProcessRow? _selected;
    private string? _selectedName;
    private List<ProcessRow> _allRows = [];
    private bool _refreshInFlight;
    private bool _bulkBusy;
    private double _savedScrollOffset;

    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(45);

    private DataGrid _processGrid = null!;
    private Button _addBtn = null!, _refreshBtn = null!;
    private MenuItem _importMenuItem = null!, _exportMenuItem = null!, _settingsMenuItem = null!, _clearAllMenuItem = null!;
    private Button _startBtn = null!, _stopBtn = null!, _restartBtn = null!, _logsBtn = null!;
    private Button _editBtn = null!, _openUrlBtn = null!, _deleteBtn = null!;
    private Button _stopAllBtn = null!, _restartAllBtn = null!;
    private Button _emptyAddBtn = null!, _emptyImportBtn = null!, _errorRetryBtn = null!;
    private TextBox _searchBox = null!;
    private ComboBox _statusFilterBox = null!;
    private TextBlock _statusText = null!, _lastUpdatedText = null!, _errorStateText = null!;
    private Border _emptyStatePanel = null!, _noMatchesPanel = null!, _errorStatePanel = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(IpcClient ipc) : this()
    {
        _ipc = ipc;
        _config = new TrayConfigService(ipc);

        _processGrid = this.FindControl<DataGrid>("ProcessGrid")!;
        _addBtn = this.FindControl<Button>("AddBtn")!;
        _refreshBtn = this.FindControl<Button>("RefreshBtn")!;
        _importMenuItem = this.FindControl<MenuItem>("ImportMenuItem")!;
        _exportMenuItem = this.FindControl<MenuItem>("ExportMenuItem")!;
        _settingsMenuItem = this.FindControl<MenuItem>("SettingsMenuItem")!;
        _clearAllMenuItem = this.FindControl<MenuItem>("ClearAllMenuItem")!;
        _startBtn = this.FindControl<Button>("StartBtn")!;
        _stopBtn = this.FindControl<Button>("StopBtn")!;
        _restartBtn = this.FindControl<Button>("RestartBtn")!;
        _logsBtn = this.FindControl<Button>("LogsBtn")!;
        _editBtn = this.FindControl<Button>("EditBtn")!;
        _openUrlBtn = this.FindControl<Button>("OpenUrlBtn")!;
        _deleteBtn = this.FindControl<Button>("DeleteBtn")!;
        _stopAllBtn = this.FindControl<Button>("StopAllBtn")!;
        _restartAllBtn = this.FindControl<Button>("RestartAllBtn")!;
        _emptyAddBtn = this.FindControl<Button>("EmptyAddBtn")!;
        _emptyImportBtn = this.FindControl<Button>("EmptyImportBtn")!;
        _errorRetryBtn = this.FindControl<Button>("ErrorRetryBtn")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _statusFilterBox = this.FindControl<ComboBox>("StatusFilterBox")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _lastUpdatedText = this.FindControl<TextBlock>("LastUpdatedText")!;
        _errorStateText = this.FindControl<TextBlock>("ErrorStateText")!;
        _emptyStatePanel = this.FindControl<Border>("EmptyStatePanel")!;
        _noMatchesPanel = this.FindControl<Border>("NoMatchesPanel")!;
        _errorStatePanel = this.FindControl<Border>("ErrorStatePanel")!;

        WireEvents();
    }

    private void WireEvents()
    {
        _processGrid.SelectionChanged += (_, _) =>
        {
            _selected = _processGrid.SelectedItem as ProcessRow;
            _selectedName = _selected?.Name;
            ApplySelection(_selected);
        };

        _addBtn.Click += async (_, _) => await OpenAdd();
        _emptyAddBtn.Click += async (_, _) => await OpenAdd();
        _refreshBtn.Click += async (_, _) => await Refresh(preserveSelection: true);
        _errorRetryBtn.Click += async (_, _) => await Refresh(preserveSelection: true);
        _importMenuItem.Click += async (_, _) => await TrayImportExportActions.ImportFromPicker(this, _config, () => Refresh(preserveSelection: true));
        _emptyImportBtn.Click += async (_, _) => await TrayImportExportActions.ImportFromPicker(this, _config, () => Refresh(preserveSelection: true));
        _exportMenuItem.Click += async (_, _) => await TrayImportExportActions.ExportToPicker(this, _config);
        _clearAllMenuItem.Click += async (_, _) => await ConfirmClearAll();
        _settingsMenuItem.Click += async (_, _) => await OpenSettings();
        _startBtn.Click += async (_, _) => await SendAction("start");
        _stopBtn.Click += async (_, _) => await SendAction("stop");
        _restartBtn.Click += async (_, _) => await SendAction("restart");
        _editBtn.Click += async (_, _) => await OpenEdit();
        _deleteBtn.Click += async (_, _) => await ConfirmDelete();
        _logsBtn.Click += (_, _) => OpenLogs();
        _openUrlBtn.Click += (_, _) => OpenSelectedUrl();
        _stopAllBtn.Click += async (_, _) => await StopAll();
        _restartAllBtn.Click += async (_, _) => await RestartAll();

        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _statusFilterBox.SelectionChanged += (_, _) => ApplyFilter();

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await Refresh(preserveSelection: true);
            }
        };

        Opened += async (_, _) =>
        {
            await Refresh(preserveSelection: false);
            StartTimer();
        };
        Closing += (_, _) => _timer?.Stop();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _timer.Tick += async (_, _) => await Refresh(preserveSelection: true);
        _timer.Start();
    }

    private void ApplySelection(ProcessRow? row)
    {
        var has = row != null;
        _startBtn.IsEnabled = has && row!.CanStart;
        _stopBtn.IsEnabled = has && row!.IsOnline;
        _restartBtn.IsEnabled = has;
        _openUrlBtn.IsEnabled = has && row!.HasUrl;
        _logsBtn.IsEnabled = has;
        _editBtn.IsEnabled = has;
        _deleteBtn.IsEnabled = has;
    }

    public async Task RefreshFromTray() => await Refresh(preserveSelection: true);

    private async Task Refresh(bool preserveSelection = false)
    {
        if (_refreshInFlight || _bulkBusy) return;
        _refreshInFlight = true;
        _refreshBtn.IsEnabled = false;
        SetStatus("Refreshing…");

        try
        {
            SaveScrollOffset();
            var keepName = preserveSelection ? _selectedName : null;

            var response = await _ipc.Send(new IpcRequest { Command = "list" });
            if (!response.Success)
            {
                _allRows = [];
                _processGrid.ItemsSource = Array.Empty<ProcessRow>();
                ShowErrorState(response.Message);
                SetStatus($"Warning: {response.Message}");
                ApplySelection(null);
                return;
            }

            HideErrorState();
            _allRows = (response.Processes ?? []).Select(p => new ProcessRow(p)).ToList();
            ApplyFilter(keepName, preserveSelection);

            _stopAllBtn.IsEnabled = _allRows.Any(r => r.IsOnline);
            _restartAllBtn.IsEnabled = _allRows.Count > 0;
            _clearAllMenuItem.IsEnabled = _allRows.Count > 0;

            _lastUpdatedText.Text = $"Last updated {DateTime.Now:HH:mm:ss}";
            if (!preserveSelection || string.IsNullOrEmpty(_statusText.Text) || _statusText.Text == "Refreshing…")
                ClearStatus();

            RestoreScrollOffset();
        }
        finally
        {
            _refreshInFlight = false;
            _refreshBtn.IsEnabled = true;
        }
    }

    private void ApplyFilter(string? keepName = null, bool preserveSelection = true)
    {
        keepName ??= _selectedName;
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        var statusTag = (_statusFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        IEnumerable<ProcessRow> filtered = _allRows;
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(r =>
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(statusTag, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(r =>
                string.Equals(r.StatusValue.ToString(), statusTag, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.ToList();
        _processGrid.ItemsSource = rows;

        var hasError = _errorStatePanel.IsVisible;
        var isEmptyList = !hasError && _allRows.Count == 0;
        var noMatches = !hasError && _allRows.Count > 0 && rows.Count == 0;
        _emptyStatePanel.IsVisible = isEmptyList;
        _noMatchesPanel.IsVisible = noMatches;
        _processGrid.IsVisible = !isEmptyList && !noMatches && !hasError;

        if (!string.IsNullOrEmpty(keepName))
        {
            var row = rows.FirstOrDefault(r => r.Name == keepName);
            if (row != null)
            {
                _processGrid.SelectedItem = row;
                _selected = row;
                _selectedName = row.Name;
                ApplySelection(row);
                return;
            }
        }

        _selected = null;
        if (!preserveSelection)
            _selectedName = null;
        ApplySelection(null);
    }

    private void ShowErrorState(string message)
    {
        _errorStateText.Text = message;
        _errorStatePanel.IsVisible = true;
        _emptyStatePanel.IsVisible = false;
        _noMatchesPanel.IsVisible = false;
        _processGrid.IsVisible = false;
        _stopAllBtn.IsEnabled = false;
        _restartAllBtn.IsEnabled = false;
        _clearAllMenuItem.IsEnabled = false;
    }

    private void HideErrorState() => _errorStatePanel.IsVisible = false;

    private void SetStatus(string text)
    {
        _statusText.Text = text;
        _statusText.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void ClearStatus()
    {
        _statusText.Text = string.Empty;
        _statusText.IsVisible = false;
    }

    private void SaveScrollOffset()
    {
        var scroll = _processGrid.FindDescendantOfType<ScrollViewer>();
        if (scroll != null)
            _savedScrollOffset = scroll.Offset.Y;
    }

    private void RestoreScrollOffset()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scroll = _processGrid.FindDescendantOfType<ScrollViewer>();
            if (scroll != null)
                scroll.Offset = new Avalonia.Vector(scroll.Offset.X, _savedScrollOffset);
        }, DispatcherPriority.Loaded);
    }

    private async Task StopAll()
    {
        var online = _allRows.Where(r => r.IsOnline).Select(r => r.Source).ToList();
        if (online.Count == 0)
        {
            SetStatus("No running processes to stop.");
            return;
        }

        var dlg = new ConfirmDialog(
            "Stop all processes?",
            $"Stop {online.Count} running process(es). They will not auto-start again until you start them.",
            "Stop all");
        if (!await dlg.ShowDialog<bool>(this)) return;

        _bulkBusy = true;
        _timer?.Stop();
        _stopAllBtn.IsEnabled = false;
        SetStatus($"Stopping {online.Count} processes…");
        try
        {
            var ok = 0;
            var fail = 0;
            string? firstError = null;
            foreach (var p in online)
            {
                var response = await _ipc.Send(new IpcRequest { Command = "stop", ProcessName = p.Name });
                if (response.Success) ok++;
                else
                {
                    fail++;
                    firstError ??= response.Message;
                }
            }

            SetStatus(fail == 0
                ? $"Stopped {ok} processes."
                : $"Stopped {ok}, failed {fail}. {firstError}");
        }
        finally
        {
            _bulkBusy = false;
            _timer?.Start();
            await Refresh(preserveSelection: true);
        }
    }

    private void OpenSelectedUrl()
    {
        var url = _selected?.Source.Url;
        if (!BrowserHelper.TryOpen(url))
            SetStatus(string.IsNullOrWhiteSpace(url)
                ? "No URL configured for this process."
                : $"Warning: Invalid URL: {url}");
    }

    private async Task RestartAll()
    {
        if (_allRows.Count == 0)
        {
            SetStatus("No processes to restart.");
            return;
        }

        var count = _allRows.Count;
        var dlg = new ConfirmDialog(
            "Restart all processes?",
            $"Restart all {count} process(es) one by one. This may interrupt running apps.",
            "Restart all");
        if (!await dlg.ShowDialog<bool>(this)) return;

        _bulkBusy = true;
        _timer?.Stop();
        _restartAllBtn.IsEnabled = false;
        SetStatus($"Restart All: restarting {count} processes one by one…");
        try
        {
            var response = await _ipc.Send(new IpcRequest { Command = "restart-all" });
            SetStatus(response.Success
                ? response.Message
                : $"Warning: {response.Message}");
        }
        finally
        {
            _bulkBusy = false;
            _timer?.Start();
            _selected = null;
            _selectedName = null;
            await Refresh(preserveSelection: false);
        }
    }

    private async Task SendAction(string command)
    {
        var name = _selected?.Name ?? _selectedName;
        if (string.IsNullOrEmpty(name)) return;

        var response = await _ipc.Send(new IpcRequest { Command = command, ProcessName = name });
        if (!response.Success)
            SetStatus($"Warning: {response.Message}");
        else if (!string.IsNullOrWhiteSpace(response.Message))
            SetStatus(response.Message);

        await Refresh(preserveSelection: true);
    }

    private async Task OpenAdd()
    {
        var dlg = new AddProcessWindow(_ipc);
        await dlg.ShowDialog(this);
        await Refresh(preserveSelection: true);
    }

    private async Task OpenEdit()
    {
        if (_selected == null) return;
        var dlg = new AddProcessWindow(_ipc, _selected.Source);
        await dlg.ShowDialog(this);
        await Refresh(preserveSelection: true);
    }

    private async Task OpenSettings()
    {
        var dlg = new SettingsWindow(_ipc);
        await dlg.ShowDialog(this);
    }

    private async Task ConfirmDelete()
    {
        if (_selected == null) return;
        var dlg = new ConfirmDialog(
            $"Delete '{_selected.Name}'?",
            "This will stop the process if running and remove it from the list.",
            "Delete");
        if (await dlg.ShowDialog<bool>(this))
            await SendAction("delete");
    }

    private async Task ConfirmClearAll()
    {
        if (_allRows.Count == 0)
        {
            SetStatus("Process list is already empty.");
            return;
        }

        var count = _allRows.Count;
        var dlg = new ConfirmDialog(
            "Clear all processes?",
            $"Stop any running apps, remove all {count} entries, and save an empty list to %ProgramData%\\NexaRun\\processes.json. Run Import JSON to load them again.",
            "Clear all");
        if (!await dlg.ShowDialog<bool>(this)) return;

        _bulkBusy = true;
        _timer?.Stop();
        _clearAllMenuItem.IsEnabled = false;
        try
        {
            var clear = await _ipc.Send(new IpcRequest { Command = "clear-all" });
            SetStatus(clear.Success ? clear.Message : $"Warning: {clear.Message}");
            _selected = null;
            _selectedName = null;
        }
        finally
        {
            _bulkBusy = false;
            _timer?.Start();
            await Refresh(preserveSelection: false);
        }
    }

    private void OpenLogs()
    {
        var name = _selected?.Name ?? _selectedName;
        if (string.IsNullOrEmpty(name)) return;
        new LogsWindow(_ipc, name).Show();
    }
}
