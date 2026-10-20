using Avalonia.Controls;
using NexaRun.Shared;
using NexaRun.Shared.Config;
using NexaRun.Tray.Views;

namespace NexaRun.Tray.Services;

public static class TrayImportExportActions
{
    public static async Task ImportBundledJson(Window? owner, TrayConfigService config, Func<Task>? onSuccess = null)
    {
        var path = TrayConfigService.FindConfigFile("nexarun-processes.json", "processes.json");
        if (path == null)
        {
            await MessageDialog.Show(owner, "File not found",
                $"Copy nexarun-processes.json next to NexaRun.exe or into:\n{NexaRunPaths.DataDir}");
            return;
        }

        await ImportFile(owner, config, path, onSuccess);
    }

    public static async Task ImportFromPicker(Window? owner, TrayConfigService config, Func<Task>? onSuccess = null)
    {
        var path = await TrayFileDialogs.PickOpenFile(
            owner,
            "Import NexaRun processes (JSON)",
            TrayConfigService.DefaultImportPath(),
            "JSON|*.json");

        if (path == null) return;

        await ImportFile(owner, config, path, onSuccess);
    }

    public static async Task ImportFromPm2(Window? owner, TrayConfigService config, Func<Task>? onSuccess = null)
    {
        var path = await TrayFileDialogs.PickOpenFile(
            owner,
            "Import PM2 dump.pm2",
            Pm2LiveImport.GetDumpPath(),
            "PM2 dump|*.pm2",
            "All files|*.*");

        if (path == null) return;

        try
        {
            var (ok, msg) = await config.ImportFromPm2Dump(path, start: false);
            await MessageDialog.Show(owner, ok ? "Import complete" : "Import failed", msg);
            if (ok && onSuccess != null)
                await onSuccess();
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(owner, "Import failed", ex.Message);
        }
    }

    public static async Task ExportToPicker(Window? owner, TrayConfigService config)
    {
        var defaultName = $"nexarun-processes-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var path = await TrayFileDialogs.PickSaveFile(owner, "Export NexaRun processes", defaultName);
        if (path == null) return;

        var (ok, msg) = await config.ExportToFile(path);
        await MessageDialog.Show(owner, ok ? "Export complete" : "Export failed", msg);
    }

    private static async Task ImportFile(Window? owner, TrayConfigService config, string path, Func<Task>? onSuccess)
    {
        var (ok, msg) = await config.ImportJsonFile(path);
        await MessageDialog.Show(owner, ok ? "Import complete" : "Import failed", msg);
        if (ok && onSuccess != null)
            await onSuccess();
    }
}
