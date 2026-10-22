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
/// <see cref="ExtensionData"/>: args, cwd, env, env_*, max_restarts, autorestart, error_file, out_file,
/// log_file, interpreter, node_args, interpreter_args, restart_delay, instances, watch, cron_restart.
/// Unsupported: exec_mode cluster semantics, ecosystem.js.
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
    public int? ReadyPort { get; set; }
    public string? ReadyFile { get; set; }
    public int? ReadyTimeoutMs { get; set; }
    /// <summary>Optional floor (ms) for crash-restart delay; exponential backoff still applies (max of both).</summary>
    public int? RestartDelayMs { get; set; }
    /// <summary>PM2-style interpreter (e.g. node). Applied by <see cref="ApplyPm2Aliases"/>.</summary>
    public string? Interpreter { get; set; }
    /// <summary>Args inserted before the script when using <see cref="Interpreter"/>.</summary>
    public string? NodeArgs { get; set; }
    /// <summary>Expand into named clones on import (<c>name</c>, <c>name-1</c>, …).</summary>
    public int? Instances { get; set; }
    public bool Watch { get; set; }
    public string? WatchPath { get; set; }
    /// <summary>5-field cron for scheduled restart (local time).</summary>
    public string? CronRestart { get; set; }

    /// <summary>Named env blocks extracted from <c>env_production</c>-style keys (not re-exported).</summary>
    [JsonIgnore]
    public Dictionary<string, Dictionary<string, string>>? NamedEnvironments { get; set; }

    /// <summary>Captures PM2-style alias fields not mapped to native properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Map ecosystem aliases, extract named env blocks, apply interpreter/node_args.
    /// Call <see cref="SelectNamedEnvironment"/> afterward when an env name is requested.
    /// </summary>
    public void ApplyPm2Aliases()
    {
        if (ExtensionData != null && ExtensionData.Count > 0)
        {
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
            if (RestartDelayMs is null or <= 0 && TryGetInt("restart_delay", out var restartDelay) && restartDelay > 0)
                RestartDelayMs = restartDelay;
            if (string.IsNullOrWhiteSpace(Interpreter) && TryGetString("interpreter", out var interpreter))
                Interpreter = interpreter;
            if (string.IsNullOrWhiteSpace(NodeArgs))
            {
                if (TryGetString("node_args", out var nodeArgs))
                    NodeArgs = nodeArgs;
                else if (TryGetString("interpreter_args", out var interpArgs))
                    NodeArgs = interpArgs;
            }

            if (Instances is null or <= 0 && TryGetInt("instances", out var instances) && instances > 0)
                Instances = instances;
            if (TryGetBool("watch", out var watch))
                Watch = watch;
            if (string.IsNullOrWhiteSpace(WatchPath) && TryGetString("watch_path", out var watchPath))
                WatchPath = watchPath;
            if (string.IsNullOrWhiteSpace(CronRestart) && TryGetString("cron_restart", out var cron))
                CronRestart = cron;

            ExtractNamedEnvironments();
            ExtensionData = null;
        }

        ApplyInterpreter();
    }

    public void SelectNamedEnvironment(string? envName)
    {
        Environment = NamedEnvironmentSelector.Merge(Environment, NamedEnvironments, envName);
    }

    private void ExtractNamedEnvironments()
    {
        if (ExtensionData == null) return;

        Dictionary<string, Dictionary<string, string>>? blocks = null;
        foreach (var (key, el) in ExtensionData)
        {
            if (!key.StartsWith("env_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (key.Equals("env", StringComparison.OrdinalIgnoreCase))
                continue;
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ToString();
            }

            if (dict.Count == 0) continue;
            blocks ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            blocks[key] = dict;
        }

        if (blocks != null)
            NamedEnvironments = blocks;
    }

    private void ApplyInterpreter()
    {
        var interpreter = Interpreter?.Trim();
        if (string.IsNullOrWhiteSpace(interpreter) ||
            interpreter.Equals("none", StringComparison.OrdinalIgnoreCase))
            return;

        var script = Script;
        if (string.IsNullOrWhiteSpace(script))
            return;

        var nodeArgs = NodeArgs?.Trim() ?? string.Empty;
        var quotedScript = QuoteIfNeeded(script);
        var merged = string.IsNullOrWhiteSpace(nodeArgs)
            ? (string.IsNullOrWhiteSpace(Arguments) ? quotedScript : $"{quotedScript} {Arguments}")
            : (string.IsNullOrWhiteSpace(Arguments)
                ? $"{nodeArgs} {quotedScript}"
                : $"{nodeArgs} {quotedScript} {Arguments}");

        Script = interpreter;
        Arguments = merged;
        Interpreter = null;
        NodeArgs = null;
    }

    private static string QuoteIfNeeded(string value) =>
        ProcessLaunchHelper.QuoteIfNeeded(value);

    private bool TryGetString(string key, out string? value)
    {
        value = null;
        if (!TryGetElement(key, out var el))
            return false;

        if (el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        // node_args / args as string array
        if (el.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(QuoteIfNeeded(s));
            }

            if (parts.Count == 0) return false;
            value = string.Join(' ', parts);
            return true;
        }

        return false;
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
