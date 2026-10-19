using Avalonia.Controls;

namespace NexaRun.Tray.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string heading, string body, string confirmLabel = "Confirm") : this()
    {
        Title = heading;
        this.FindControl<TextBlock>("HeadingText")!.Text = heading;
        this.FindControl<TextBlock>("BodyText")!.Text = body;
        var confirmBtn = this.FindControl<Button>("ConfirmBtn")!;
        confirmBtn.Content = confirmLabel;
        confirmBtn.Click += (_, _) => Close(true);
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close(false);
    }
}
