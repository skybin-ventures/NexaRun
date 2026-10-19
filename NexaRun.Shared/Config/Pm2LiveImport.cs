using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Imports currently online PM2 apps via <c>pm2 jlist</c>, optionally enriching definitions from dump.pm2.
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

    public static Pm2LiveImportResult ReadOnline(string? onlyApp = null)
    {
        var jlistJson = RunPm2Jlist();
        var dumpJson = TryReadDumpFile();
        var result = FromJlistAndDump(jlistJson, dumpJson);

        if (string.IsNullOrWhiteSpace(onlyApp))
            return result;

        var filtered = result.Options
            .Where(o => string.Equals(o.Name, onlyApp, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == 0)
            throw new InvalidOperationException($"No online PM2 app named '{onlyApp}'.");

        return new Pm2LiveImportResult
        {
            Options = filtered,
            Summary = $"Mapped 1 online app ('{filtered[0].Name}') from PM2.",
            EnrichedFromDump = Math.Min(1, result.EnrichedFromDump),
            ClusterDuplicatesSkipped = 0
        };
    }

    /// <summary>Pure merge/mapping API for tests (no process spawn).</summary>
    public static Pm2LiveImportResult FromJlistAndDump(string jlistJson, string? dumpJson)
    {
        if (string.IsNullOrWhiteSpace(jlistJson))
            throw new InvalidOperationException("PM2 did not return a process list.");

        List<Pm2ProcessEntry> jlist;
        try
        {
            jlist = JsonSerializer.Deserialize<List<Pm2ProcessEntry>>(jlistJson, JsonOptions)
                    ?? throw new InvalidOperationException("Unexpected pm2 jlist output.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Unexpected pm2 jlist output.", ex);
        }

        Dictionary<string, Pm2ProcessEntry>? dumpByName = null;
        if (!string.IsNullOrWhiteSpace(dumpJson))
        {
            try
            {
                var dump = JsonSerializer.Deserialize<List<Pm2ProcessEntry>>(dumpJson, JsonOptions);
                if (dump != null)
                {
                    dumpByName = new Dictionary<string, Pm2ProcessEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in dump)
                    {
                        var name = entry.ResolveName();
                        if (string.IsNullOrWhiteSpace(name) || dumpByName.ContainsKey(name))
                            continue;
                        dumpByName[name] = entry;
                    }
                }
            }
            catch (JsonException)
            {
                dumpByName = null;
            }
        }

        var options = new List<StartOptions>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enrichedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clusterSkipped = 0;

        foreach (var entry in jlist)
        {
            var name = entry.ResolveName();
            if (string.IsNullOrWhiteSpace(name) || IsInternalPm2(name, entry))
                continue;

            if (!entry.IsOnline())
                continue;

            if (!seen.Add(name))
            {
                clusterSkipped++;
                continue;
            }

            Pm2ProcessEntry source = entry;
            if (dumpByName != null && dumpByName.TryGetValue(name, out var dumpEntry))
            {
                source = dumpEntry;
                enrichedNames.Add(name);
            }

            options.Add(MapToStartOptions(name, source));
        }

        if (options.Count == 0)
            throw new InvalidOperationException("No running PM2 apps to import.");

        var enriched = enrichedNames.Count;
        var dumpNote = dumpByName == null
            ? "definitions from jlist only"
            : $"{enriched} enriched from dump.pm2";
        var clusterNote = clusterSkipped > 0
            ? $"; skipped {clusterSkipped} cluster duplicate(s)"
            : string.Empty;

        return new Pm2LiveImportResult
        {
            Options = options,
            Summary = $"Mapped {options.Count} online app(s); {dumpNote}{clusterNote}.",
            EnrichedFromDump = enriched,
            ClusterDuplicatesSkipped = clusterSkipped
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

    private static string? TryReadDumpFile()
    {
        try
        {
            var path = GetDumpPath();
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string RunPm2Jlist()
    {
        ProcessStartInfo psi;
        try
        {
            psi = ProcessLaunchHelper.CreateStartInfo(
                "pm2",
                "jlist",
                Environment.CurrentDirectory,
                redirectOutput: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PM2 not found. Install PM2 and ensure it is on PATH.", ex);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException(
                    "PM2 not found. Install PM2 and ensure it is on PATH.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "PM2 not found. Install PM2 and ensure it is on PATH.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new InvalidOperationException("PM2 did not respond in time.");
        }

        // Ensure async readers finish
        process.WaitForExit();

        var output = stdout.ToString().Trim();
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            var err = stderr.ToString().Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? "PM2 did not return a process list."
                    : $"PM2 did not return a process list: {err}");
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("PM2 did not return a process list.");

        return output;
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
            Environment = FilterEnv(env.Env)
        };

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

        public bool IsOnline()
        {
            var status = Pm2Env?.Status;
            return string.Equals(status, "online", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class Pm2Env
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

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

        [JsonPropertyName("pmx_module")]
        public bool? Module { get; set; }
    }
}
