using Avalonia.Controls;

namespace NexaRun.Tray.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string heading, string body) : this()
    {
        Title = heading;
        this.FindControl<TextBlock>("HeadingText")!.Text = heading;
        this.FindControl<TextBlock>("BodyText")!.Text = body;
        this.FindControl<Button>("OkBtn")!.Click += (_, _) => Close();
    }

    public static async Task Show(Window? owner, string heading, string body)
    {
        var dlg = new MessageDialog(heading, body);
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }
}
