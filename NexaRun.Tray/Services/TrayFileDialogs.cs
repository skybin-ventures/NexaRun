using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NexaRun.Tray.Helpers;

namespace NexaRun.Tray.Services;

public static class TrayFileDialogs
{
    public static async Task<string?> PickOpenFile(
        Window? owner,
        string title,
        string? suggestedPath = null,
        params string[] patterns)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Tray menu with no open window: Win32 dialog (Avalonia needs a real HWND).
        if (owner == null && OperatingSystem.IsWindows())
        {
            // Let the native tray menu finish dismissing before showing the dialog.
            await Task.Delay(120);
            return Win32FileDialog.Open(title, suggestedPath, patterns);
        }

        var (top, hidden) = await GetTopLevel(owner);
        try
        {
            var filters = patterns.Select(p =>
            {
                var parts = p.Split('|', 2);
                var exts = parts.Length > 1
                    ? parts[1].Split(';').Select(e => e.StartsWith('*') ? e : $"*{e}").ToArray()
                    : ["*"];
                return new FilePickerFileType(parts[0]) { Patterns = exts };
            }).ToList();

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters
            };

            if (!string.IsNullOrEmpty(suggestedPath))
            {
                var dir = Path.GetDirectoryName(suggestedPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    options.SuggestedStartLocation = await top.StorageProvider.TryGetFolderFromPathAsync(dir);
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0) return null;
            return files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
        }
        finally
        {
            hidden?.Close();
        }
    }

    public static async Task<string?> PickSaveFile(Window? owner, string title, string defaultName)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });

        if (owner == null && OperatingSystem.IsWindows())
        {
            await Task.Delay(120);
            return Win32FileDialog.Save(title, defaultName, null);
        }

        var (top, hidden) = await GetTopLevel(owner);
        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultName,
                DefaultExtension = "json",
                FileTypeChoices =
                [
                    new FilePickerFileType("NexaRun export") { Patterns = ["*.json"] }
                ]
            });

            if (file == null) return null;
            var path = file.TryGetLocalPath() ?? file.Path.LocalPath;
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : path + ".json";
        }
        finally
        {
            hidden?.Close();
        }
    }

    private static async Task<(TopLevel Top, Window? Hidden)> GetTopLevel(Window? owner)
    {
        if (owner != null)
        {
            owner.Activate();
            return (owner, null);
        }

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new Window
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            Opacity = 0,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new Avalonia.PixelPoint(-32000, -32000)
        };
        hidden.Opened += (_, _) => opened.TrySetResult();
        hidden.Show();
        hidden.Activate();
        await Task.WhenAny(opened.Task, Task.Delay(500));
        await Task.Delay(50);
        return (hidden, hidden);
    }
}
