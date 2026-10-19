using System.Text.Json;
using NexaRun.Shared.Models;

namespace NexaRun.Shared.Config;

public static class ProcessConfigTransfer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static ProcessExportBundle FromProcesses(IEnumerable<NexaProcess> processes) =>
        new()
        {
            ExportedAt = DateTime.UtcNow,
            ExportedFrom = Environment.MachineName,
            Apps = processes.Select(FromProcess).ToList()
        };

    public static ProcessDefinition FromProcess(NexaProcess p) =>
        new()
        {
            Name = p.Name,
            Script = p.ExecutablePath,
            Arguments = p.Arguments,
            WorkingDirectory = p.WorkingDirectory,
            AutoRestart = p.AutoRestart,
            MaxRestartAttempts = p.MaxRestartAttempts,
            MaxCpuPercent = p.MaxCpuPercent,
            MaxMemoryMb = p.MaxMemoryMb,
            OutLogFile = p.OutLogFile,
            ErrorLogFile = p.ErrorLogFile,
            LogFile = p.LogFile,
            LogTimestamps = p.LogTimestamps,
            Environment = p.Environment,
            Url = p.Url
        };

    public static StartOptions ToStartOptions(ProcessDefinition def)
    {
        var options = new StartOptions
        {
            Name = def.Name,
            ExecutablePath = def.Script,
            Arguments = def.Arguments,
            WorkingDirectory = def.WorkingDirectory,
            AutoRestart = def.AutoRestart,
            MaxRestartAttempts = def.MaxRestartAttempts,
            MaxCpuPercent = def.MaxCpuPercent,
            MaxMemoryMb = def.MaxMemoryMb,
            OutLogFile = def.OutLogFile,
            ErrorLogFile = def.ErrorLogFile,
            CombinedLogFile = def.LogFile,
            LogTimestamps = def.LogTimestamps,
            Environment = def.Environment,
            Url = def.Url
        };
        ProcessUrlHelper.ApplyUrl(options);
        return options;
    }

    public static List<StartOptions> FromImportFile(string path, string? onlyApp = null)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .json process files are supported.");

        var json = File.ReadAllText(path);
        var bundle = JsonSerializer.Deserialize<ProcessExportBundle>(json, JsonOptions)
                     ?? throw new InvalidOperationException("JSON file is empty or invalid.");

        if (bundle.Apps.Count == 0)
            throw new InvalidOperationException("JSON file must contain a non-empty 'apps' array.");

        var apps = bundle.Apps;
        if (!string.IsNullOrWhiteSpace(onlyApp))
        {
            apps = apps
                .Where(a => string.Equals(a.Name, onlyApp, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (apps.Count == 0)
                throw new InvalidOperationException($"No app named '{onlyApp}' in file.");
        }

        foreach (var app in apps)
            app.ApplyPm2Aliases();

        return apps.Select(ToStartOptions).ToList();
    }

    public static async Task WriteBundleAsync(ProcessExportBundle bundle, string path)
    {
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, json);
    }
}
