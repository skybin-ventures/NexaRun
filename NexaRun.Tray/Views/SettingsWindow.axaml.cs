using Avalonia.Controls;
using NexaRun.Shared;
using NexaRun.Shared.Config;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Tray.Views;

public partial class SettingsWindow : Window
{
    private IpcClient _ipc = null!;
    private NexaRunSettings? _loaded;

    private CheckBox _recoveryEnabledBox = null!;
    private TextBox _recoveryIntervalBox = null!;
    private StackPanel _recoveryFieldsPanel = null!;
    private CheckBox _emailEnabledBox = null!;
    private StackPanel _emailFieldsPanel = null!;
    private ComboBox _emailProviderBox = null!;
    private TextBox _emailFromBox = null!, _emailToBox = null!;
    private TextBox _awsRegionBox = null!, _awsAccessKeyBox = null!, _awsSecretBox = null!;
    private StackPanel _sesApiPanel = null!, _sesSmtpPanel = null!;
    private TextBox _sesSmtpUserBox = null!, _sesSmtpPasswordBox = null!, _sesSmtpPortBox = null!;
    private CheckBox _sesSmtpTlsBox = null!;
    private TextBlock _errorText = null!, _loadWarningText = null!;
    private Button _saveBtn = null!, _cancelBtn = null!;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(IpcClient ipc) : this()
    {
        _ipc = ipc;
        BindControls();
        WireEvents();
        Opened += async (_, _) => await LoadSettings();
    }

    private void BindControls()
    {
        _recoveryEnabledBox = this.FindControl<CheckBox>("RecoveryEnabledBox")!;
        _recoveryIntervalBox = this.FindControl<TextBox>("RecoveryIntervalBox")!;
        _recoveryFieldsPanel = this.FindControl<StackPanel>("RecoveryFieldsPanel")!;
        _emailEnabledBox = this.FindControl<CheckBox>("EmailEnabledBox")!;
        _emailFieldsPanel = this.FindControl<StackPanel>("EmailFieldsPanel")!;
        _emailProviderBox = this.FindControl<ComboBox>("EmailProviderBox")!;
        _emailFromBox = this.FindControl<TextBox>("EmailFromBox")!;
        _emailToBox = this.FindControl<TextBox>("EmailToBox")!;
        _awsRegionBox = this.FindControl<TextBox>("AwsRegionBox")!;
        _awsAccessKeyBox = this.FindControl<TextBox>("AwsAccessKeyBox")!;
        _awsSecretBox = this.FindControl<TextBox>("AwsSecretBox")!;
        _sesApiPanel = this.FindControl<StackPanel>("SesApiPanel")!;
        _sesSmtpPanel = this.FindControl<StackPanel>("SesSmtpPanel")!;
        _sesSmtpUserBox = this.FindControl<TextBox>("SesSmtpUserBox")!;
        _sesSmtpPasswordBox = this.FindControl<TextBox>("SesSmtpPasswordBox")!;
        _sesSmtpPortBox = this.FindControl<TextBox>("SesSmtpPortBox")!;
        _sesSmtpTlsBox = this.FindControl<CheckBox>("SesSmtpTlsBox")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
        _loadWarningText = this.FindControl<TextBlock>("LoadWarningText")!;
        _saveBtn = this.FindControl<Button>("SaveBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
    }

    private void WireEvents()
    {
        _saveBtn.Click += async (_, _) => await SaveSettings();
        _cancelBtn.Click += (_, _) => Close();
        _emailProviderBox.SelectionChanged += (_, _) => UpdateProviderPanels();
        _recoveryEnabledBox.IsCheckedChanged += (_, _) => UpdateDisclosure();
        _emailEnabledBox.IsCheckedChanged += (_, _) => UpdateDisclosure();
    }

    private void UpdateDisclosure()
    {
        _recoveryFieldsPanel.IsEnabled = _recoveryEnabledBox.IsChecked == true;
        _emailFieldsPanel.IsEnabled = _emailEnabledBox.IsChecked == true;
    }

    private void UpdateProviderPanels()
    {
        var useSmtp = GetSelectedProvider() == EmailAlertProvider.SesSmtp;
        _sesApiPanel.IsVisible = !useSmtp;
        _sesSmtpPanel.IsVisible = useSmtp;
    }

    private string GetSelectedProvider()
    {
        if (_emailProviderBox.SelectedItem is ComboBoxItem { Tag: string tag })
            return EmailAlertProvider.Normalize(tag);
        return EmailAlertProvider.SesApi;
    }

    private void SelectProvider(string provider)
    {
        var normalized = EmailAlertProvider.Normalize(provider);
        foreach (var item in _emailProviderBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && EmailAlertProvider.Normalize(tag) == normalized)
            {
                _emailProviderBox.SelectedItem = item;
                break;
            }
        }

        UpdateProviderPanels();
    }

    private async Task LoadSettings()
    {
        _loadWarningText.IsVisible = false;
        var response = await _ipc.Send(new IpcRequest { Command = "get-settings" });
        if (!response.Success)
        {
            _loadWarningText.Text =
                $"Warning: Could not load settings from daemon ({response.Message}). Showing local settings.json.";
            _loadWarningText.IsVisible = true;
        }

        _loaded = response.Settings ?? await NexaRunSettingsStore.LoadAsync();

        _recoveryEnabledBox.IsChecked = _loaded.FailedRecoveryEnabled;
        _recoveryIntervalBox.Text = _loaded.FailedRecoveryIntervalMinutes.ToString();
        _emailEnabledBox.IsChecked = _loaded.EmailAlertEnabled;
        _emailFromBox.Text = _loaded.AlertEmailFrom ?? string.Empty;
        _emailToBox.Text = _loaded.AlertEmailTo ?? string.Empty;
        _awsRegionBox.Text = string.IsNullOrWhiteSpace(_loaded.AwsRegion) ? "us-east-1" : _loaded.AwsRegion;
        _awsAccessKeyBox.Text = _loaded.AwsAccessKeyId ?? string.Empty;
        _awsSecretBox.Text = string.Empty;
        _sesSmtpUserBox.Text = _loaded.SesSmtpUsername ?? string.Empty;
        _sesSmtpPasswordBox.Text = string.Empty;
        _sesSmtpPortBox.Text = (_loaded.SesSmtpPort > 0 ? _loaded.SesSmtpPort : 587).ToString();
        _sesSmtpTlsBox.IsChecked = _loaded.SesSmtpUseTls;

        SelectProvider(_loaded.EmailProvider);
        UpdateDisclosure();
    }

    private async Task SaveSettings()
    {
        _errorText.IsVisible = false;

        if (!int.TryParse(_recoveryIntervalBox.Text?.Trim(), out var intervalMinutes)
            || intervalMinutes < ProcessDefaults.MinFailedRecoveryIntervalMinutes)
        {
            ShowError($"Recovery interval must be at least {ProcessDefaults.MinFailedRecoveryIntervalMinutes} minutes.");
            return;
        }

        var emailEnabled = _emailEnabledBox.IsChecked == true;
        var provider = GetSelectedProvider();
        var iamSecret = string.IsNullOrWhiteSpace(_awsSecretBox.Text)
            ? _loaded?.AwsSecretAccessKey
            : _awsSecretBox.Text.Trim();
        var smtpPassword = string.IsNullOrWhiteSpace(_sesSmtpPasswordBox.Text)
            ? _loaded?.SesSmtpPassword
            : _sesSmtpPasswordBox.Text;

        if (emailEnabled)
        {
            if (string.IsNullOrWhiteSpace(_emailFromBox.Text))
            {
                ShowError("Enter a From address verified in AWS SES.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_emailToBox.Text))
            {
                ShowError("Enter a To (alert recipient) address.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_awsRegionBox.Text))
            {
                ShowError("Enter the AWS region (e.g. us-east-1).");
                return;
            }

            if (provider == EmailAlertProvider.SesApi)
            {
                if (string.IsNullOrWhiteSpace(_awsAccessKeyBox.Text))
                {
                    ShowError("Enter the AWS access key ID for SES API.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(iamSecret))
                {
                    ShowError("Enter the IAM secret access key (or leave blank only if one is already saved).");
                    return;
                }
            }
            else
            {
                if (!int.TryParse(_sesSmtpPortBox.Text?.Trim(), out var smtpPort) || smtpPort is < 1 or > 65535)
                {
                    ShowError("SMTP port must be between 1 and 65535.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_sesSmtpUserBox.Text))
                {
                    ShowError("Enter the SES SMTP username.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(smtpPassword))
                {
                    ShowError("Enter the SES SMTP password (or leave blank only if one is already saved).");
                    return;
                }
            }
        }

        var smtpPortValue = 587;
        if (provider == EmailAlertProvider.SesSmtp)
            int.TryParse(_sesSmtpPortBox.Text?.Trim(), out smtpPortValue);

        var settings = new NexaRunSettings
        {
            FailedRecoveryEnabled = _recoveryEnabledBox.IsChecked == true,
            FailedRecoveryIntervalMinutes = intervalMinutes,
            EmailAlertEnabled = emailEnabled,
            EmailProvider = provider,
            AlertEmailFrom = _emailFromBox.Text?.Trim(),
            AlertEmailTo = _emailToBox.Text?.Trim(),
            AwsRegion = _awsRegionBox.Text?.Trim() ?? "us-east-1",
            AwsAccessKeyId = _awsAccessKeyBox.Text?.Trim(),
            AwsSecretAccessKey = iamSecret,
            SesSmtpUsername = _sesSmtpUserBox.Text?.Trim(),
            SesSmtpPassword = smtpPassword,
            SesSmtpPort = smtpPortValue > 0 ? smtpPortValue : 587,
            SesSmtpUseTls = _sesSmtpTlsBox.IsChecked == true
        };

        _saveBtn.IsEnabled = false;
        try
        {
            var response = await _ipc.Send(new IpcRequest { Command = "set-settings", Settings = settings });
            if (!response.Success)
            {
                await NexaRunSettingsStore.SaveAsync(settings);
                ShowError($"Daemon could not apply settings ({response.Message}). Saved to settings.json for when the service restarts.");
                return;
            }

            await MessageDialog.Show(this, "Settings saved", response.Message);
            Close();
        }
        finally
        {
            _saveBtn.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }
}
