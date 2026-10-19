using NexaRun.Shared.Config;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Tray.Services;

public class TrayConfigService(IpcClient ipc)
{
    public async Task<(bool success, string message)> ImportJsonFile(string path, bool start = false)
    {
        var options = ProcessConfigTransfer.FromImportFile(path);
        return await SendImport(options, start);
    }

    public async Task<(bool success, string message)> ImportFromPm2(bool start = false)
    {
        var result = Pm2LiveImport.ReadOnline();
        var (ok, msg) = await SendImport(result.Options, start);
        if (!ok)
            return (false, msg);
        return (true, $"{result.Summary}\n{msg}");
    }

    public async Task<(bool success, string message)> ExportToFile(string path)
    {
        var response = await ipc.Send(new IpcRequest { Command = "list" });
        if (!response.Success || response.Processes == null)
            return (false, response.Message);

        var bundle = ProcessConfigTransfer.FromProcesses(response.Processes);
        await ProcessConfigTransfer.WriteBundleAsync(bundle, path);
        return (true, $"Exported {bundle.Apps.Count} process(es) to {path}");
    }

    public static string? FindConfigFile(params string[] fileNames)
    {
        foreach (var dir in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     NexaRun.Shared.NexaRunPaths.DataDir
                 })
        {
            foreach (var name in fileNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    public static string DefaultImportPath() =>
        FindConfigFile("nexarun-processes.json", "processes.json")
        ?? Path.Combine(AppContext.BaseDirectory, "nexarun-processes.json");

    private async Task<(bool success, string message)> SendImport(List<StartOptions> options, bool start)
    {
        var response = await ipc.Send(new IpcRequest
        {
            Command = "import",
            BatchOptions = options,
            StartAfterImport = start
        });
        return (response.Success, response.Message);
    }
}
