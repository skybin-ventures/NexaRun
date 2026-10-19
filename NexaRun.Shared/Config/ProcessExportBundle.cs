using System.Text.Json;
using System.Text.Json.Serialization;
using NexaRun.Shared;

namespace NexaRun.Shared.Config;

public class ProcessExportBundle
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string? ExportedFrom { get; set; }
    public List<ProcessDefinition> Apps { get; set; } = [];
}

/// <summary>
/// Native NexaRun app definition. Also accepts a small PM2 ecosystem apps[] field subset via
/// <see cref="ExtensionData"/>: args, cwd, env, max_restarts, autorestart, error_file, out_file, log_file.
/// Unsupported: instances, exec_mode, watch, cron_restart, ecosystem.js.
/// </summary>
public class ProcessDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool AutoRestart { get; set; } = true;
    public int MaxRestartAttempts { get; set; } = ProcessDefaults.MaxRestartAttempts;
    public double? MaxCpuPercent { get; set; }
    public long? MaxMemoryMb { get; set; }
    public string? OutLogFile { get; set; }
    public string? ErrorLogFile { get; set; }
    public string? LogFile { get; set; }
    public bool LogTimestamps { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public string? Url { get; set; }

    /// <summary>Captures PM2-style alias fields not mapped to native properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public void ApplyPm2Aliases()
    {
        if (ExtensionData == null || ExtensionData.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(Arguments) && TryGetString("args", out var args))
            Arguments = args!;
        if (string.IsNullOrWhiteSpace(WorkingDirectory) && TryGetString("cwd", out var cwd))
            WorkingDirectory = cwd!;
        if ((Environment == null || Environment.Count == 0) && TryGetEnv("env", out var env))
            Environment = env;
        if (TryGetInt("max_restarts", out var maxRestarts) && maxRestarts > 0)
            MaxRestartAttempts = maxRestarts;
        if (TryGetBool("autorestart", out var autorestart))
            AutoRestart = autorestart;
        if (string.IsNullOrWhiteSpace(ErrorLogFile) && TryGetString("error_file", out var err))
            ErrorLogFile = err;
        if (string.IsNullOrWhiteSpace(OutLogFile) && TryGetString("out_file", out var outFile))
            OutLogFile = outFile;
        if (string.IsNullOrWhiteSpace(LogFile) && TryGetString("log_file", out var logFile))
            LogFile = logFile;

        ExtensionData = null;
    }

    private bool TryGetString(string key, out string? value)
    {
        value = null;
        if (!TryGetElement(key, out var el) || el.ValueKind != JsonValueKind.String)
            return false;
        value = el.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private bool TryGetInt(string key, out int value)
    {
        value = 0;
        if (!TryGetElement(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            return true;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value))
            return true;
        return false;
    }

    private bool TryGetBool(string key, out bool value)
    {
        value = false;
        if (!TryGetElement(key, out var el))
            return false;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }

        return el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out value);
    }

    private bool TryGetEnv(string key, out Dictionary<string, string>? env)
    {
        env = null;
        if (!TryGetElement(key, out var el) || el.ValueKind != JsonValueKind.Object)
            return false;

        env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
            env[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString() ?? string.Empty
                : prop.Value.ToString();
        return env.Count > 0;
    }

    private bool TryGetElement(string key, out JsonElement el)
    {
        el = default;
        if (ExtensionData == null)
            return false;

        foreach (var pair in ExtensionData)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                el = pair.Value;
                return true;
            }
        }

        return false;
    }
}
