using Avalonia.Controls;

namespace NexaRun.Tray.Views;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

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
        confirmBtn.Click += (_, _) =>
        {
            Confirmed = true;
            Close(true);
        };
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) =>
        {
            Confirmed = false;
            Close(false);
        };
    }

    public static async Task<bool> Show(Window? owner, string heading, string body, string confirmLabel = "Confirm")
    {
        var dlg = new ConfirmDialog(heading, body, confirmLabel);
        if (owner != null)
            return await dlg.ShowDialog<bool>(owner);

        var tcs = new TaskCompletionSource<bool>();
        dlg.Closed += (_, _) => tcs.TrySetResult(dlg.Confirmed);
        dlg.Show();
        return await tcs.Task;
    }
}
