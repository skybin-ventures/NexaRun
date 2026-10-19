using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NexaRun.Shared.Ipc;
using NexaRun.Tray.Services;

namespace NexaRun.Tray.Views;

public partial class LogsWindow : Window
{
    private IpcClient _ipc = null!;
    private string _processName = "";
    private DispatcherTimer? _timer;
    private TrayLogViewer _viewer = null!;

    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(2);

    private TextBlock _titleText = null!, _errorBanner = null!;
    private Button _refreshBtn = null!, _clearLogBtn = null!;
    private CheckBox _autoScrollBox = null!, _liveUpdateBox = null!;
    private ScrollViewer _scroller = null!;
    private TextBox _logBox = null!;

    public LogsWindow()
    {
        InitializeComponent();
    }

    public LogsWindow(IpcClient ipc, string processName) : this()
    {
        _ipc = ipc;
        _processName = processName;

        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _errorBanner = this.FindControl<TextBlock>("ErrorBanner")!;
        _refreshBtn = this.FindControl<Button>("RefreshBtn")!;
        _clearLogBtn = this.FindControl<Button>("ClearLogBtn")!;
        _autoScrollBox = this.FindControl<CheckBox>("AutoScrollBox")!;
        _liveUpdateBox = this.FindControl<CheckBox>("LiveUpdateBox")!;
        _scroller = this.FindControl<ScrollViewer>("Scroller")!;
        _logBox = this.FindControl<TextBox>("LogBox")!;

        Title = $"Logs — {processName}";
        _titleText.Text = processName;

        _viewer = new TrayLogViewer(
            _ipc,
            () => _processName,
            _logBox,
            _scroller,
            _autoScrollBox,
            _errorBanner);

        _refreshBtn.Click += async (_, _) => await _viewer.Refresh();
        _clearLogBtn.Click += async (_, _) => await _viewer.ClearLogs(this);
        _liveUpdateBox.IsCheckedChanged += (_, _) => UpdateTimer();

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await _viewer.Refresh();
            }
        };

        Opened += async (_, _) =>
        {
            await _viewer.Refresh();
            StartTimer();
        };
        Closing += (_, _) => _timer?.Stop();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = LiveInterval };
        _timer.Tick += async (_, _) =>
        {
            if (_liveUpdateBox.IsChecked == true)
                await _viewer.Refresh();
        };
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (_timer == null) return;
        if (_liveUpdateBox.IsChecked == true)
            _timer.Start();
        else
            _timer.Stop();
    }
}
