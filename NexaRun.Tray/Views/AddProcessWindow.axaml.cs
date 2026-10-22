using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using NexaRun.Tray.Services;

namespace NexaRun.Tray.Views;

public partial class AddProcessWindow : Window
{
    private IpcClient _ipc = null!;
    private TrayConfigService _config = null!;
    private bool _isEdit;

    private TextBox _nameBox = null!, _execBox = null!, _argsBox = null!, _cwdBox = null!;
    private TextBox _maxCpuBox = null!, _maxMemBox = null!, _maxRestartsBox = null!, _urlBox = null!;
    private TextBox _envBox = null!, _readyPortBox = null!, _readyFileBox = null!, _readyTimeoutBox = null!;
    private TextBox _watchPathBox = null!, _cronRestartBox = null!, _restartDelayBox = null!;
    private CheckBox _autoRestartBox = null!, _logTimestampsBox = null!, _watchBox = null!;
    private Button _startBtn = null!, _cancelBtn = null!, _importJsonBtn = null!, _browseBtn = null!, _browseCwdBtn = null!, _browseWatchPathBtn = null!;
    private TextBlock _errorText = null!, _nameHint = null!;
    private TextBlock _maxCpuError = null!, _maxMemError = null!, _maxRestartsError = null!;
    private TextBlock _readyPortError = null!, _readyTimeoutError = null!;
    private TextBlock _cronRestartError = null!, _restartDelayError = null!;

    public AddProcessWindow()
    {
        InitializeComponent();
    }

    public AddProcessWindow(IpcClient ipc, NexaProcess? existing = null) : this()
    {
        _ipc = ipc;
        _config = new TrayConfigService(ipc);
        _isEdit = existing != null;

        _nameBox = this.FindControl<TextBox>("NameBox")!;
        _execBox = this.FindControl<TextBox>("ExecBox")!;
        _argsBox = this.FindControl<TextBox>("ArgsBox")!;
        _cwdBox = this.FindControl<TextBox>("CwdBox")!;
        _maxCpuBox = this.FindControl<TextBox>("MaxCpuBox")!;
        _maxMemBox = this.FindControl<TextBox>("MaxMemBox")!;
        _maxRestartsBox = this.FindControl<TextBox>("MaxRestartsBox")!;
        _urlBox = this.FindControl<TextBox>("UrlBox")!;
        _envBox = this.FindControl<TextBox>("EnvBox")!;
        _readyPortBox = this.FindControl<TextBox>("ReadyPortBox")!;
        _readyFileBox = this.FindControl<TextBox>("ReadyFileBox")!;
        _readyTimeoutBox = this.FindControl<TextBox>("ReadyTimeoutBox")!;
        _watchBox = this.FindControl<CheckBox>("WatchBox")!;
        _watchPathBox = this.FindControl<TextBox>("WatchPathBox")!;
        _cronRestartBox = this.FindControl<TextBox>("CronRestartBox")!;
        _restartDelayBox = this.FindControl<TextBox>("RestartDelayBox")!;
        _autoRestartBox = this.FindControl<CheckBox>("AutoRestartBox")!;
        _logTimestampsBox = this.FindControl<CheckBox>("LogTimestampsBox")!;
        _startBtn = this.FindControl<Button>("StartBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _importJsonBtn = this.FindControl<Button>("ImportJsonBtn")!;
        _browseBtn = this.FindControl<Button>("BrowseBtn")!;
        _browseCwdBtn = this.FindControl<Button>("BrowseCwdBtn")!;
        _browseWatchPathBtn = this.FindControl<Button>("BrowseWatchPathBtn")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
        _nameHint = this.FindControl<TextBlock>("NameHint")!;
        _maxCpuError = this.FindControl<TextBlock>("MaxCpuError")!;
        _maxMemError = this.FindControl<TextBlock>("MaxMemError")!;
        _maxRestartsError = this.FindControl<TextBlock>("MaxRestartsError")!;
        _readyPortError = this.FindControl<TextBlock>("ReadyPortError")!;
        _readyTimeoutError = this.FindControl<TextBlock>("ReadyTimeoutError")!;
        _cronRestartError = this.FindControl<TextBlock>("CronRestartError")!;
        _restartDelayError = this.FindControl<TextBlock>("RestartDelayError")!;

        if (existing != null)
        {
            Title = "Edit Process";
            _importJsonBtn.IsVisible = false;
            _startBtn.Content = "Save Changes";
            _nameBox.Text = existing.Name;
            _nameBox.IsEnabled = false;
            _nameHint.IsVisible = true;
            _execBox.Text = existing.ExecutablePath;
            _argsBox.Text = existing.Arguments;
            _cwdBox.Text = existing.WorkingDirectory;
            _autoRestartBox.IsChecked = existing.AutoRestart;
            _maxCpuBox.Text = existing.MaxCpuPercent?.ToString() ?? string.Empty;
            _maxMemBox.Text = existing.MaxMemoryMb?.ToString() ?? string.Empty;
            _maxRestartsBox.Text = existing.MaxRestartAttempts.ToString();
            _logTimestampsBox.IsChecked = existing.LogTimestamps;
            _urlBox.Text = existing.Url ?? string.Empty;
            _envBox.Text = FormatEnvironment(existing.Environment);
            _readyPortBox.Text = existing.ReadyPort?.ToString() ?? string.Empty;
            _readyFileBox.Text = existing.ReadyFile ?? string.Empty;
            _readyTimeoutBox.Text = existing.ReadyTimeoutMs?.ToString() ?? string.Empty;
            _watchBox.IsChecked = existing.Watch;
            _watchPathBox.Text = existing.WatchPath ?? string.Empty;
            _cronRestartBox.Text = existing.CronRestart ?? string.Empty;
            _restartDelayBox.Text = existing.RestartDelayMs?.ToString() ?? string.Empty;
        }
        else
        {
            _maxRestartsBox.Text = ProcessDefaults.MaxRestartAttempts.ToString();
        }

        WireEvents();
    }

    private void WireEvents()
    {
        _cancelBtn.Click += (_, _) => Close(false);
        _startBtn.Click += async (_, _) => await Submit();
        _importJsonBtn.Click += async (_, _) => await ImportJson();
        _browseBtn.Click += async (_, _) => await BrowseFile(_execBox);
        _browseCwdBtn.Click += async (_, _) => await BrowseFolder(_cwdBox);
        _browseWatchPathBtn.Click += async (_, _) => await BrowseFolder(_watchPathBox);
    }

    private async Task Submit()
    {
        ClearFieldErrors();
        var name = _nameBox.Text?.Trim();
        var exec = _execBox.Text?.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exec))
        {
            ShowError("Name and Executable are required.");
            return;
        }

        if (!TryParseOptionalPositiveDouble(_maxCpuBox.Text, out var maxCpu, out var cpuError))
        {
            _maxCpuError.Text = cpuError;
            _maxCpuError.IsVisible = true;
            ShowError(cpuError!);
            return;
        }

        if (!TryParseOptionalPositiveLong(_maxMemBox.Text, out var maxMem, out var memError))
        {
            _maxMemError.Text = memError;
            _maxMemError.IsVisible = true;
            ShowError(memError!);
            return;
        }

        if (!TryParsePositiveInt(_maxRestartsBox.Text, ProcessDefaults.MaxRestartAttempts, out var maxRestarts, out var restartsError))
        {
            _maxRestartsError.Text = restartsError;
            _maxRestartsError.IsVisible = true;
            ShowError(restartsError!);
            return;
        }

        if (!TryParseOptionalPositiveInt(_readyPortBox.Text, out var readyPort, out var readyPortError))
        {
            _readyPortError.Text = readyPortError;
            _readyPortError.IsVisible = true;
            ShowError(readyPortError!);
            return;
        }

        if (!TryParseOptionalPositiveInt(_readyTimeoutBox.Text, out var readyTimeout, out var readyTimeoutError))
        {
            _readyTimeoutError.Text = readyTimeoutError;
            _readyTimeoutError.IsVisible = true;
            ShowError(readyTimeoutError!);
            return;
        }

        if (!TryParseOptionalPositiveInt(_restartDelayBox.Text, out var restartDelay, out var restartDelayError))
        {
            _restartDelayError.Text = restartDelayError;
            _restartDelayError.IsVisible = true;
            ShowError(restartDelayError!);
            return;
        }

        var cronText = string.IsNullOrWhiteSpace(_cronRestartBox.Text) ? null : _cronRestartBox.Text.Trim();
        if (cronText != null && !CronExpression.TryValidate(cronText, out var cronError))
        {
            _cronRestartError.Text = cronError;
            _cronRestartError.IsVisible = true;
            ShowError(cronError!);
            return;
        }

        var envText = _envBox.Text;
        Dictionary<string, string>? environment;
        if (string.IsNullOrWhiteSpace(envText))
        {
            environment = null;
        }
        else
        {
            var lines = envText.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            environment = EnvironmentPairParser.Parse(lines);
            if (environment == null && lines.Length > 0)
            {
                ShowError("Environment lines must be KEY=VALUE (one per line).");
                return;
            }
        }

        _startBtn.IsEnabled = false;
        _startBtn.Content = _isEdit ? "Saving…" : "Starting…";

        var readyFile = string.IsNullOrWhiteSpace(_readyFileBox.Text) ? null : _readyFileBox.Text.Trim();

        var response = await _ipc.Send(new IpcRequest
        {
            Command = _isEdit ? "update" : "start",
            Options = new StartOptions
            {
                Name = name,
                ExecutablePath = exec,
                Arguments = _argsBox.Text?.Trim() ?? string.Empty,
                WorkingDirectory = _cwdBox.Text?.Trim() ?? string.Empty,
                AutoRestart = _autoRestartBox.IsChecked ?? true,
                MaxCpuPercent = maxCpu,
                MaxMemoryMb = maxMem,
                MaxRestartAttempts = maxRestarts,
                LogTimestamps = _logTimestampsBox.IsChecked ?? false,
                Url = string.IsNullOrWhiteSpace(_urlBox.Text) ? null : _urlBox.Text.Trim(),
                Environment = environment ?? (_isEdit ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : null),
                ReadyPort = readyPort,
                ReadyFile = readyFile,
                ReadyTimeoutMs = readyTimeout,
                Watch = _watchBox.IsChecked ?? false,
                WatchPath = string.IsNullOrWhiteSpace(_watchPathBox.Text) ? null : _watchPathBox.Text.Trim(),
                CronRestart = cronText,
                RestartDelayMs = restartDelay
            }
        });

        if (response.Success)
        {
            Close(true);
        }
        else
        {
            ShowError(response.Message);
            _startBtn.IsEnabled = true;
            _startBtn.Content = _isEdit ? "Save Changes" : "Save & Start";
        }
    }

    private static string FormatEnvironment(Dictionary<string, string>? env)
    {
        if (env == null || env.Count == 0) return string.Empty;
        return string.Join(Environment.NewLine, env.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static bool TryParseOptionalPositiveDouble(string? text, out double? value, out string? error)
    {
        value = null;
        error = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;
        if (!double.TryParse(trimmed, out var parsed) || parsed <= 0)
        {
            error = "Max CPU must be a number greater than 0, or leave blank for no limit.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseOptionalPositiveLong(string? text, out long? value, out string? error)
    {
        value = null;
        error = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;
        if (!long.TryParse(trimmed, out var parsed) || parsed <= 0)
        {
            error = "Max Memory must be a whole number greater than 0, or leave blank for no limit.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseOptionalPositiveInt(string? text, out int? value, out string? error)
    {
        value = null;
        error = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;
        if (!int.TryParse(trimmed, out var parsed) || parsed <= 0)
        {
            error = "Must be a whole number greater than 0, or leave blank.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParsePositiveInt(string? text, int fallback, out int value, out string? error)
    {
        error = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            value = fallback;
            return true;
        }

        if (!int.TryParse(trimmed, out value) || value <= 0)
        {
            error = "Max restart attempts must be a whole number greater than 0.";
            value = fallback;
            return false;
        }

        return true;
    }

    private void ClearFieldErrors()
    {
        _maxCpuError.IsVisible = false;
        _maxMemError.IsVisible = false;
        _maxRestartsError.IsVisible = false;
        _readyPortError.IsVisible = false;
        _readyTimeoutError.IsVisible = false;
        _cronRestartError.IsVisible = false;
        _restartDelayError.IsVisible = false;
        _errorText.IsVisible = false;
    }

    private async Task ImportJson()
    {
        _importJsonBtn.IsEnabled = false;
        try
        {
            await TrayImportExportActions.ImportFromPicker(this, _config, onSuccess: () =>
            {
                Close(true);
                return Task.CompletedTask;
            });
        }
        finally
        {
            _importJsonBtn.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    private async Task BrowseFile(TextBox target)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Executable",
            AllowMultiple = false
        });
        if (files.Count > 0)
            target.Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
    }

    private async Task BrowseFolder(TextBox target)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Working Directory"
        });
        if (folders.Count > 0)
            target.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }
}
