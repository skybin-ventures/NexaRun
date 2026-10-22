using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Shared.Config;

public sealed class Pm2LiveImportResult
{
    public required List<StartOptions> Options { get; init; }
    public required string Summary { get; init; }
    public int EnrichedFromDump { get; init; }
    public int ClusterDuplicatesSkipped { get; init; }
}

/// <summary>
/// Imports PM2 apps from a <c>dump.pm2</c> file (no local <c>pm2</c> required).
/// </summary>
public static class Pm2LiveImport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> Pm2EnvSkipKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unique_id", "pm_id", "instance_var", "status", "pm_uptime", "restart_time",
        "unstable_restarts", "created_at", "axm_actions", "axm_monitor", "axm_options",
        "axm_dynamic", "vizion_running", "km_link", "pmx", "automation", "module",
        "windowsHide", "username", "NODE_APP_INSTANCE"
    };

    public static bool LooksLikeDumpPath(string path) =>
        path.EndsWith(".pm2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Import saved apps from a PM2 <c>dump.pm2</c> file.</summary>
    public static Pm2LiveImportResult ReadDumpFile(string path, string? onlyApp = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Dump file path is required.");

        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new InvalidOperationException($"Dump file not found: {full}");

        string dumpJson;
        try
        {
            dumpJson = File.ReadAllText(full);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not read dump file: {full}", ex);
        }

        return FilterOnly(FromDump(dumpJson), onlyApp);
    }

    /// <summary>Pure dump mapping API for tests (no file I/O).</summary>
    public static Pm2LiveImportResult FromDump(string dumpJson)
    {
        if (string.IsNullOrWhiteSpace(dumpJson))
            throw new InvalidOperationException("PM2 dump was empty.");

        List<Pm2ProcessEntry> dump;
        try
        {
            dump = JsonSerializer.Deserialize<List<Pm2ProcessEntry>>(dumpJson, JsonOptions)
                   ?? throw new InvalidOperationException("Unexpected PM2 dump format.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Unexpected PM2 dump format.", ex);
        }

        var options = new List<StartOptions>();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clusterClones = 0;

        foreach (var entry in dump)
        {
            var baseName = entry.ResolveName();
            if (string.IsNullOrWhiteSpace(baseName) || IsInternalPm2(baseName, entry))
                continue;

            nameCounts.TryGetValue(baseName, out var count);
            nameCounts[baseName] = count + 1;
            var name = count == 0 ? baseName : $"{baseName}-{count}";
            if (count > 0)
                clusterClones++;

            var opt = MapToStartOptions(name, entry);
            opt.Environment ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!opt.Environment.ContainsKey("NODE_APP_INSTANCE"))
                opt.Environment["NODE_APP_INSTANCE"] = count.ToString();
            options.Add(opt);
        }

        if (options.Count == 0)
            throw new InvalidOperationException("No apps found in PM2 dump to import.");

        var clusterNote = clusterClones > 0
            ? $"; expanded {clusterClones} cluster instance(s) to named clones (name-1, …)"
            : string.Empty;

        return new Pm2LiveImportResult
        {
            Options = options,
            Summary = $"Mapped {options.Count} app(s) from dump.pm2{clusterNote}.",
            EnrichedFromDump = options.Count,
            ClusterDuplicatesSkipped = 0
        };
    }

    public static string GetDumpPath()
    {
        var pm2Home = Environment.GetEnvironmentVariable("PM2_HOME");
        if (!string.IsNullOrWhiteSpace(pm2Home))
            return Path.Combine(pm2Home, "dump.pm2");

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".pm2", "dump.pm2");
    }

    private static Pm2LiveImportResult FilterOnly(Pm2LiveImportResult result, string? onlyApp)
    {
        if (string.IsNullOrWhiteSpace(onlyApp))
            return result;

        var filtered = result.Options
            .Where(o => string.Equals(o.Name, onlyApp, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == 0)
            throw new InvalidOperationException($"No app in PM2 dump named '{onlyApp}'.");

        return new Pm2LiveImportResult
        {
            Options = filtered,
            Summary = $"Mapped 1 app ('{filtered[0].Name}') from dump.pm2.",
            EnrichedFromDump = Math.Min(1, result.EnrichedFromDump),
            ClusterDuplicatesSkipped = 0
        };
    }

    private static bool IsInternalPm2(string name, Pm2ProcessEntry entry)
    {
        if (name.Equals("pm2-logrotate", StringComparison.OrdinalIgnoreCase))
            return true;

        var env = entry.Pm2Env;
        if (env?.Module == true)
            return true;

        return false;
    }

    private static StartOptions MapToStartOptions(string name, Pm2ProcessEntry entry)
    {
        var env = entry.Pm2Env ?? new Pm2Env();
        var execPath = env.PmExecPath ?? entry.Name ?? string.Empty;
        var interpreter = env.ExecInterpreter?.Trim() ?? string.Empty;
        var isNone = string.IsNullOrWhiteSpace(interpreter) ||
                     interpreter.Equals("none", StringComparison.OrdinalIgnoreCase);

        var scriptArgs = JoinArgs(env.Args);
        var interpreterArgs = JoinArgs(env.NodeArgs ?? env.InterpreterArgs);

        string executable;
        string arguments;
        if (isNone)
        {
            executable = execPath;
            arguments = JoinParts(interpreterArgs, scriptArgs);
        }
        else
        {
            executable = interpreter;
            var quotedScript = QuoteIfNeeded(execPath);
            arguments = JoinParts(interpreterArgs, quotedScript, scriptArgs);
        }

        var options = new StartOptions
        {
            Name = name,
            ExecutablePath = executable,
            Arguments = arguments,
            WorkingDirectory = env.PmCwd ?? string.Empty,
            AutoRestart = env.Autorestart ?? true,
            MaxRestartAttempts = env.MaxRestarts is > 0
                ? env.MaxRestarts.Value
                : ProcessDefaults.MaxRestartAttempts,
            MaxMemoryMb = ParseMaxMemoryMb(env.MaxMemoryRestart),
            OutLogFile = NullIfBlank(env.PmOutLogPath),
            ErrorLogFile = NullIfBlank(env.PmErrLogPath),
            CombinedLogFile = NullIfBlank(env.PmLogPath),
            Environment = FilterEnv(env.Env),
            Watch = env.Watch == true,
            WatchPath = NullIfBlank(env.WatchPath),
            CronRestart = NullIfBlank(env.CronRestart),
            RestartDelayMs = env.RestartDelay is > 0 ? env.RestartDelay : null
        };

        if (!string.IsNullOrWhiteSpace(options.CronRestart) &&
            !CronExpression.TryValidate(options.CronRestart, out var cronError))
        {
            throw new InvalidOperationException(
                $"App '{name}' has invalid cron_restart: {cronError}");
        }

        ProcessUrlHelper.ApplyUrl(options);
        return options;
    }

    private static Dictionary<string, string>? FilterEnv(Dictionary<string, JsonElement>? env)
    {
        if (env == null || env.Count == 0)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in env)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (key.StartsWith("pm_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Pm2EnvSkipKeys.Contains(key))
                continue;

            result[key] = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        return result.Count > 0 ? result : null;
    }

    private static long? ParseMaxMemoryMb(JsonElement? element)
    {
        if (element == null)
            return null;

        var el = element.Value;
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var bytes))
            return bytes > 0 ? Math.Max(1, bytes / (1024 * 1024)) : null;

        if (el.ValueKind != JsonValueKind.String)
            return null;

        var s = el.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(s))
            return null;

        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asBytes))
            return asBytes > 0 ? Math.Max(1, asBytes / (1024 * 1024)) : null;

        var suffix = char.ToUpperInvariant(s[^1]);
        if (!char.IsLetter(suffix))
            return null;

        if (!double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return null;

        return suffix switch
        {
            'K' => Math.Max(1, (long)(n / 1024)),
            'M' => Math.Max(1, (long)n),
            'G' => Math.Max(1, (long)(n * 1024)),
            _ => null
        };
    }

    private static string JoinArgs(JsonElement? args)
    {
        if (args == null)
            return string.Empty;

        var el = args.Value;
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return string.Empty;

        if (el.ValueKind == JsonValueKind.String)
            return el.GetString()?.Trim() ?? string.Empty;

        if (el.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            var s = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                parts.Add(QuoteIfNeeded(s));
        }

        return string.Join(' ', parts);
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ') && !value.StartsWith('"') ? $"\"{value}\"" : value;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class Pm2ProcessEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("pm2_env")]
        public Pm2Env? Pm2Env { get; set; }

        public string? ResolveName()
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;
            return Pm2Env?.Name;
        }
    }

    private sealed class Pm2Env
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("pm_exec_path")]
        public string? PmExecPath { get; set; }

        [JsonPropertyName("exec_interpreter")]
        public string? ExecInterpreter { get; set; }

        [JsonPropertyName("pm_cwd")]
        public string? PmCwd { get; set; }

        [JsonPropertyName("args")]
        public JsonElement? Args { get; set; }

        [JsonPropertyName("node_args")]
        public JsonElement? NodeArgs { get; set; }

        [JsonPropertyName("interpreter_args")]
        public JsonElement? InterpreterArgs { get; set; }

        [JsonPropertyName("autorestart")]
        public bool? Autorestart { get; set; }

        [JsonPropertyName("max_restarts")]
        public int? MaxRestarts { get; set; }

        [JsonPropertyName("pm_out_log_path")]
        public string? PmOutLogPath { get; set; }

        [JsonPropertyName("pm_err_log_path")]
        public string? PmErrLogPath { get; set; }

        [JsonPropertyName("pm_log_path")]
        public string? PmLogPath { get; set; }

        [JsonPropertyName("max_memory_restart")]
        public JsonElement? MaxMemoryRestart { get; set; }

        [JsonPropertyName("env")]
        public Dictionary<string, JsonElement>? Env { get; set; }

        [JsonPropertyName("watch")]
        public bool? Watch { get; set; }

        [JsonPropertyName("watch_path")]
        public string? WatchPath { get; set; }

        [JsonPropertyName("cron_restart")]
        public string? CronRestart { get; set; }

        [JsonPropertyName("restart_delay")]
        public int? RestartDelay { get; set; }

        [JsonPropertyName("pmx_module")]
        public bool? Module { get; set; }
    }
}
