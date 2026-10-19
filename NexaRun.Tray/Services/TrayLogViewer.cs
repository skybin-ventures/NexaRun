using Avalonia.Controls;
using Avalonia.Threading;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Tray.Services;

/// <summary>Shared log fetch/display behavior for LogsWindow and Dashboard logs tab.</summary>
public sealed class TrayLogViewer
{
    public const int DefaultLogLines = 300;

    private readonly IpcClient _ipc;
    private readonly Func<string?> _processName;
    private readonly TextBox _logBox;
    private readonly ScrollViewer _scroller;
    private readonly CheckBox _autoScrollBox;
    private readonly TextBlock? _errorBanner;
    private double _savedOffset;
    private bool _refreshInFlight;

    public TrayLogViewer(
        IpcClient ipc,
        Func<string?> processName,
        TextBox logBox,
        ScrollViewer scroller,
        CheckBox autoScrollBox,
        TextBlock? errorBanner = null)
    {
        _ipc = ipc;
        _processName = processName;
        _logBox = logBox;
        _scroller = scroller;
        _autoScrollBox = autoScrollBox;
        _errorBanner = errorBanner;
    }

    public async Task Refresh()
    {
        if (_refreshInFlight) return;
        var name = _processName();
        if (string.IsNullOrEmpty(name)) return;

        _refreshInFlight = true;
        try
        {
            var stickToEnd = _autoScrollBox.IsChecked == true;
            if (!stickToEnd)
                _savedOffset = _scroller.Offset.Y;

            var response = await _ipc.Send(new IpcRequest
            {
                Command = "logs",
                ProcessName = name,
                LogLines = DefaultLogLines
            });

            if (!response.Success)
            {
                SetError(response.Message);
                return;
            }

            ClearError();
            _logBox.Text = response.Logs ?? string.Empty;

            Dispatcher.UIThread.Post(() =>
            {
                if (stickToEnd)
                    _scroller.ScrollToEnd();
                else
                    _scroller.Offset = new Avalonia.Vector(_scroller.Offset.X, _savedOffset);
            }, DispatcherPriority.Loaded);
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    public async Task ClearLogs(Window owner)
    {
        var name = _processName();
        if (string.IsNullOrEmpty(name)) return;

        var dlg = new Views.ConfirmDialog(
            $"Clear logs for '{name}'?",
            "Deletes the on-disk log files for this process. New output will continue to be captured.",
            "Clear logs");
        if (!await dlg.ShowDialog<bool>(owner)) return;

        var response = await _ipc.Send(new IpcRequest
        {
            Command = "clear-logs",
            ProcessName = name
        });

        if (!response.Success)
        {
            SetError(response.Message);
            return;
        }

        ClearError();
        await Refresh();
    }

    private void SetError(string message)
    {
        if (_errorBanner != null)
        {
            _errorBanner.Text = $"Warning: {message}";
            _errorBanner.IsVisible = true;
        }
        else
        {
            // Fallback when no banner: keep prior log text and prepend is avoided — set status via tag
            _logBox.Tag = message;
        }
    }

    private void ClearError()
    {
        if (_errorBanner != null)
        {
            _errorBanner.Text = string.Empty;
            _errorBanner.IsVisible = false;
        }

        _logBox.Tag = null;
    }

    public string? LastError =>
        _errorBanner is { IsVisible: true } ? _errorBanner.Text : _logBox.Tag as string;
}
