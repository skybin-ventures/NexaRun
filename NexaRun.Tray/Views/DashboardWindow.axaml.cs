using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using NexaRun.Tray.Services;
using NexaRun.Tray.ViewModels;

namespace NexaRun.Tray.Views;

public partial class DashboardWindow : Window
{
    private IpcClient _ipc = null!;
    private DispatcherTimer? _timer;
    private string? _selectedName;
    private TrayLogViewer _logViewer = null!;

    private TextBlock _statusText = null!;
    private Button _refreshBtn = null!, _restartBtn = null!, _openUrlBtn = null!, _logsBtn = null!;
    private ListBox _processList = null!;
    private Border _statsBar = null!;
    private TextBlock _detailStatus = null!, _detailReason = null!;
    private TextBlock _detailPid = null!, _detailMemory = null!, _detailRestarts = null!;
    private DockPanel _historyPanel = null!;
    private TextBlock _historyTitle = null!;
    private ItemsControl _uptimeBars = null!;
    private DataGrid _runGrid = null!;
    private TextBlock _emptyText = null!, _emptyListText = null!;
    private StackPanel _detailTabs = null!;
    private Button _historyTabBtn = null!, _logsTabBtn = null!;
    private DockPanel _logsPanel = null!;
    private TextBlock _logsTitle = null!, _logsErrorBanner = null!;
    private Button _logsRefreshBtn = null!, _logsClearBtn = null!;
    private CheckBox _logsAutoScrollBox = null!;
    private CheckBox _logsLiveBox = null!;
    private ScrollViewer _logsScroller = null!;
    private TextBox _logsBox = null!;

    private enum DetailView { History, Logs }
    private DetailView _detailView = DetailView.History;

    private static readonly TimeSpan ListRefreshInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LogsLiveInterval = TimeSpan.FromSeconds(8);

    public DashboardWindow()
    {
        InitializeComponent();
    }

    public DashboardWindow(IpcClient ipc) : this()
    {
        _ipc = ipc;

        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _refreshBtn = this.FindControl<Button>("RefreshBtn")!;
        _restartBtn = this.FindControl<Button>("RestartBtn")!;
        _openUrlBtn = this.FindControl<Button>("OpenUrlBtn")!;
        _logsBtn = this.FindControl<Button>("LogsBtn")!;
        _processList = this.FindControl<ListBox>("ProcessList")!;
        _statsBar = this.FindControl<Border>("StatsBar")!;
        _detailStatus = this.FindControl<TextBlock>("DetailStatus")!;
        _detailReason = this.FindControl<TextBlock>("DetailReason")!;
        _detailPid = this.FindControl<TextBlock>("DetailPid")!;
        _detailMemory = this.FindControl<TextBlock>("DetailMemory")!;
        _detailRestarts = this.FindControl<TextBlock>("DetailRestarts")!;
        _historyPanel = this.FindControl<DockPanel>("HistoryPanel")!;
        _historyTitle = this.FindControl<TextBlock>("HistoryTitle")!;
        _uptimeBars = this.FindControl<ItemsControl>("UptimeBars")!;
        _runGrid = this.FindControl<DataGrid>("RunGrid")!;
        _emptyText = this.FindControl<TextBlock>("EmptyText")!;
        _emptyListText = this.FindControl<TextBlock>("EmptyListText")!;
        _detailTabs = this.FindControl<StackPanel>("DetailTabs")!;
        _historyTabBtn = this.FindControl<Button>("HistoryTabBtn")!;
        _logsTabBtn = this.FindControl<Button>("LogsTabBtn")!;
        _logsPanel = this.FindControl<DockPanel>("LogsPanel")!;
        _logsTitle = this.FindControl<TextBlock>("LogsTitle")!;
        _logsErrorBanner = this.FindControl<TextBlock>("LogsErrorBanner")!;
        _logsRefreshBtn = this.FindControl<Button>("LogsRefreshBtn")!;
        _logsClearBtn = this.FindControl<Button>("LogsClearBtn")!;
        _logsAutoScrollBox = this.FindControl<CheckBox>("LogsAutoScrollBox")!;
        _logsLiveBox = this.FindControl<CheckBox>("LogsLiveBox")!;
        _logsScroller = this.FindControl<ScrollViewer>("LogsScroller")!;
        _logsBox = this.FindControl<TextBox>("LogsBox")!;

        _logViewer = new TrayLogViewer(
            _ipc,
            () => _selectedName,
            _logsBox,
            _logsScroller,
            _logsAutoScrollBox,
            _logsErrorBanner);

        WireEvents();
    }

    private void WireEvents()
    {
        _refreshBtn.Click += async (_, _) => await RefreshAll();
        _restartBtn.Click += async (_, _) => await RestartSelected();
        _openUrlBtn.Click += (_, _) => OpenSelectedUrl();
        _logsBtn.Click += async (_, _) => await ShowLogsTab();
        _historyTabBtn.Click += async (_, _) => await ShowHistoryTab();
        _logsTabBtn.Click += async (_, _) => await ShowLogsTab();
        _logsRefreshBtn.Click += async (_, _) => await _logViewer.Refresh();
        _logsClearBtn.Click += async (_, _) => await _logViewer.ClearLogs(this);
        _logsLiveBox.IsCheckedChanged += (_, _) =>
        {
            if (_timer != null && _detailView == DetailView.Logs)
                _timer.Interval = _logsLiveBox.IsChecked == true ? LogsLiveInterval : ListRefreshInterval;
        };

        _processList.SelectionChanged += async (_, _) =>
        {
            var item = _processList.SelectedItem as DashboardProcessItem;
            _selectedName = item?.Name;
            if (item != null)
                await ShowDetail(item);
            else
                ShowEmpty();
        };

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await RefreshAll();
            }
        };

        Opened += async (_, _) =>
        {
            await RefreshProcesses();
            StartTimer();
        };
        Closing += (_, _) => _timer?.Stop();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = ListRefreshInterval };
        _timer.Tick += async (_, _) => await OnBackgroundTick();
        _timer.Start();
    }

    private async Task OnBackgroundTick()
    {
        await RefreshProcesses();
        UpdateSelectedStats();

        if (_detailView == DetailView.Logs && _logsLiveBox.IsChecked == true)
            await _logViewer.Refresh();
    }

    private async Task RefreshAll()
    {
        await RefreshProcesses();
        if (_selectedName == null) return;

        var item = FindSelectedItem();
        if (item == null) return;

        if (_detailView == DetailView.Logs)
            await _logViewer.Refresh();
        else
            await ShowDetail(item);
    }

    private DashboardProcessItem? FindSelectedItem() =>
        (_processList.ItemsSource as List<DashboardProcessItem>)
            ?.FirstOrDefault(x => x.Name == _selectedName)
        ?? _processList.SelectedItem as DashboardProcessItem;

    private void UpdateSelectedStats()
    {
        var item = FindSelectedItem();
        if (item == null) return;

        _detailStatus.Text = item.StatusLabel;
        _detailStatus.Foreground = Avalonia.Media.SolidColorBrush.Parse(item.StatusColor);
        var reason = item.StatusReason;
        _detailReason.Text = reason ?? string.Empty;
        _detailReason.IsVisible = !string.IsNullOrWhiteSpace(reason);
        _detailPid.Text = item.Pid;
        _detailMemory.Text = item.Memory;
        _detailRestarts.Text = item.Restarts.ToString();
        SetActionButtonsEnabled(item);
    }

    private void OpenSelectedUrl()
    {
        var item = FindSelectedItem();
        var url = item?.Source.Url;
        if (!BrowserHelper.TryOpen(url))
            _statusText.Text = string.IsNullOrWhiteSpace(url)
                ? "No URL configured for this process."
                : $"Warning: Invalid URL: {url}";
    }

    private async Task RefreshProcesses()
    {
        var response = await _ipc.Send(new IpcRequest { Command = "list" });
        _statusText.Text = response.Success ? string.Empty : $"Warning: {response.Message}";
        if (!response.Success) return;

        var items = (response.Processes ?? [])
            .Select(p => new DashboardProcessItem(p))
            .ToList();

        var prevSelected = _selectedName;
        _processList.ItemsSource = items;
        _emptyListText.IsVisible = items.Count == 0;

        if (prevSelected != null)
        {
            var match = items.FirstOrDefault(x => x.Name == prevSelected);
            if (match != null && (_processList.SelectedItem as DashboardProcessItem)?.Name != match.Name)
                _processList.SelectedItem = match;
        }
    }

    private async Task ShowDetail(DashboardProcessItem item)
    {
        UpdateSelectedStats();
        _statsBar.IsVisible = true;
        _emptyText.IsVisible = false;
        SetActionButtonsEnabled(item);

        var resp = await _ipc.Send(new IpcRequest { Command = "history", ProcessName = item.Name });
        var history = resp.RunHistory ?? [];

        _historyTitle.Text = item.Name;
        _logsTitle.Text = $"{item.Name} — log output";
        ApplyDetailView();

        var bars = BuildUptimeBars(history);
        _uptimeBars.ItemsSource = bars;
        _runGrid.ItemsSource = history.Select(r => new RunHistoryRow(r)).ToList();

        if (_detailView == DetailView.Logs)
            await _logViewer.Refresh();
    }

    private void ShowEmpty()
    {
        _statsBar.IsVisible = false;
        _detailTabs.IsVisible = false;
        _historyPanel.IsVisible = false;
        _logsPanel.IsVisible = false;
        _emptyText.IsVisible = true;
        _detailReason.IsVisible = false;
        SetActionButtonsEnabled(null);
    }

    private void SetActionButtonsEnabled(DashboardProcessItem? item)
    {
        var has = item != null;
        _restartBtn.IsEnabled = has;
        _logsBtn.IsEnabled = has;
        _openUrlBtn.IsEnabled = has && item!.HasUrl;
    }

    private async Task RestartSelected()
    {
        if (string.IsNullOrEmpty(_selectedName)) return;

        _restartBtn.IsEnabled = false;
        _statusText.Text = $"Restarting {_selectedName}…";
        try
        {
            var response = await _ipc.Send(new IpcRequest
            {
                Command = "restart",
                ProcessName = _selectedName
            });

            if (!response.Success)
            {
                _statusText.Text = $"Warning: {response.Message}";
                return;
            }

            _statusText.Text = $"Restarted {_selectedName}.";
            await RefreshAll();
        }
        finally
        {
            SetActionButtonsEnabled(FindSelectedItem());
        }
    }

    private async Task ShowHistoryTab()
    {
        _detailView = DetailView.History;
        ApplyDetailView();
        if (_timer != null)
            _timer.Interval = ListRefreshInterval;
        var item = _processList.SelectedItem as DashboardProcessItem;
        if (item != null)
            await ShowDetail(item);
    }

    private async Task ShowLogsTab()
    {
        _detailView = DetailView.Logs;
        ApplyDetailView();
        if (_timer != null)
            _timer.Interval = _logsLiveBox.IsChecked == true ? LogsLiveInterval : ListRefreshInterval;
        await _logViewer.Refresh();
    }

    private void ApplyDetailView()
    {
        var hasSelection = _processList.SelectedItem != null;
        _detailTabs.IsVisible = hasSelection;
        _emptyText.IsVisible = !hasSelection;

        var history = _detailView == DetailView.History;
        _historyPanel.IsVisible = hasSelection && history;
        _logsPanel.IsVisible = hasSelection && !history;

        SetTabActive(_historyTabBtn, history);
        SetTabActive(_logsTabBtn, !history);
    }

    private static void SetTabActive(Button button, bool active)
    {
        button.Classes.Set("active", active);
    }

    private static List<UptimeDayBar> BuildUptimeBars(List<ProcessRun> history)
    {
        var bars = new List<UptimeDayBar>();
        var today = DateTime.UtcNow.Date;

        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var dayEnd = day.AddDays(1);
            var seconds = 86400.0;

            double uptimeSec = 0;
            foreach (var run in history)
            {
                var runStart = run.StartedAt > day ? run.StartedAt : day;
                var runEnd = (run.EndedAt ?? DateTime.UtcNow) < dayEnd
                    ? (run.EndedAt ?? DateTime.UtcNow)
                    : dayEnd;
                if (runEnd > runStart)
                    uptimeSec += (runEnd - runStart).TotalSeconds;
            }

            bars.Add(new UptimeDayBar(day, Math.Min(uptimeSec / seconds, 1.0)));
        }

        return bars;
    }
}
