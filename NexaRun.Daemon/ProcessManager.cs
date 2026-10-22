using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NexaRun.Shared;
using NexaRun.Shared.Config;
using NexaRun.Shared.Models;

namespace NexaRun.Daemon;

public class ProcessManager
{
    private readonly List<NexaProcess> _processes = [];
    private readonly Dictionary<int, Process> _runningProcesses = [];
    private readonly Dictionary<int, ProcessLogSession> _logSessions = [];
    private readonly Dictionary<int, TimeSpan> _lastCpuTime = [];
    private readonly Dictionary<int, DateTime> _lastCpuSample = [];
    private readonly Dictionary<int, int> _resourceBreachStreak = [];
    private readonly List<ProcessRun> _runHistory = [];
    private readonly string _dataDir;
    private readonly string _logDir;
    private readonly string _persistPath;
    private readonly string _historyPath;
    private readonly ILogger<ProcessManager> _logger;
    private readonly ProcessAlertService _alerts;
    private NexaRunSettings _settings = new();
    private readonly Dictionary<string, ProcessStatus> _previousStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CancellationTokenSource> _pendingRestarts = new();
    private int _nextId = 0;
    private DateTime _lastFailedRecoveryUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProcessManager(ILogger<ProcessManager> logger, ProcessAlertService alerts)
    {
        _logger = logger;
        _alerts = alerts;
        NexaRunPaths.EnsureDirectories();
        _dataDir = NexaRunPaths.DataDir;
        _logDir = NexaRunPaths.LogDir;
        _persistPath = NexaRunPaths.ProcessesFile;
        _historyPath = NexaRunPaths.HistoryFile;
    }

    public async Task Load()
    {
        if (File.Exists(_historyPath))
        {
            try
            {
                var hJson = await File.ReadAllTextAsync(_historyPath);
                var loaded = JsonSerializer.Deserialize<List<ProcessRun>>(hJson);
                if (loaded != null)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-7);
                    _runHistory.AddRange(loaded.Where(r => r.StartedAt >= cutoff));
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load run history"); }
        }

        if (!File.Exists(_persistPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_persistPath);
            var loaded = JsonSerializer.Deserialize<List<NexaProcess>>(json);
            if (loaded == null) return;

            await _lock.WaitAsync();
            try
            {
                var hasDesiredStatusField = json.Contains("desiredStatus", StringComparison.OrdinalIgnoreCase);

                foreach (var p in loaded)
                {
                    var savedPid = p.Pid;
                    var savedStatus = p.Status;

                    if (!hasDesiredStatusField)
                    {
                        // Pre-DesiredStatus files: Online/Errored meant the user wanted the app running.
                        p.DesiredStatus = savedStatus is ProcessStatus.Online or ProcessStatus.Errored
                            or ProcessStatus.Starting or ProcessStatus.Stopping
                            ? ProcessStatus.Online
                            : ProcessStatus.Stopped;
                    }
                    else if (p.DesiredStatus is not (ProcessStatus.Online or ProcessStatus.Stopped))
                    {
                        p.DesiredStatus = ProcessStatus.Stopped;
                    }

                    p.Status = ProcessStatus.Stopped;
                    p.Pid = null;
                    FixLegacyCombinedLogPath(p);
                    ProcessUrlHelper.ApplyUrl(p);

                    if (p.Id >= _nextId) _nextId = p.Id + 1;

                    if (savedPid.HasValue)
                        TryReattach(p, savedPid.Value);

                    _processes.Add(p);
                }
            }
            finally { _lock.Release(); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted processes");
        }

        await ReloadSettings();
        await _lock.WaitAsync();
        try
        {
            foreach (var p in _processes)
                _previousStatus[p.Name] = p.Status;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// After Load, start any process with DesiredStatus Online that is not already attached.
    /// Distinct from merely starting the Windows service.
    /// </summary>
    public async Task RestoreDesiredOnline()
    {
        List<NexaProcess> toStart;
        await _lock.WaitAsync();
        try
        {
            toStart = _processes
                .Where(p => p.DesiredStatus == ProcessStatus.Online && p.Status != ProcessStatus.Online)
                .ToList();
        }
        finally { _lock.Release(); }

        for (var i = 0; i < toStart.Count; i++)
        {
            var p = toStart[i];
            var (ok, message, _) = await Start(ToStartOptions(p));
            if (ok)
                _logger.LogInformation("Boot restore started '{Name}'", p.Name);
            else
                _logger.LogWarning("Boot restore failed for '{Name}': {Message}", p.Name, message);

            if (i < toStart.Count - 1)
                await Task.Delay(ProcessDefaults.StartSettleMs);
        }
    }

    private void TryReattach(NexaProcess p, int savedPid)
    {
        try
        {
            var osProc = Process.GetProcessById(savedPid);
            if (osProc.HasExited)
                return;

            if (!ProcessPidValidator.MatchesFingerprint(osProc, p.StartedAt, p.ExecutablePath))
            {
                _logger.LogWarning(
                    "Stale PID {Pid} for '{Name}' rejected (fingerprint mismatch)",
                    savedPid, p.Name);
                return;
            }

            p.Pid = savedPid;
            p.Status = ProcessStatus.Online;
            _runningProcesses[p.Id] = osProc;
            _logger.LogInformation("Reattached to '{Name}' (PID {Pid})", p.Name, savedPid);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not reattach PID {Pid} for '{Name}'", savedPid, p.Name);
        }
    }

    private void CancelPendingRestart(int processId)
    {
        if (_pendingRestarts.Remove(processId, out var cts))
        {
            try { cts.Cancel(); }
            catch { /* ignore */ }
            cts.Dispose();
        }
    }

    private CancellationToken BeginPendingRestart(int processId)
    {
        CancelPendingRestart(processId);
        var cts = new CancellationTokenSource();
        _pendingRestarts[processId] = cts;
        return cts.Token;
    }

    public async Task ReloadSettings() =>
        _settings = await NexaRunSettingsStore.LoadAsync();

    public Task<NexaRunSettings> GetSettings() =>
        NexaRunSettingsStore.LoadAsync();

    public async Task<(bool success, string message)> SaveSettings(NexaRunSettings settings)
    {
        await NexaRunSettingsStore.SaveAsync(settings);
        _settings = settings;
        _logger.LogInformation(
            "Settings updated: recovery={Recovery} every {Minutes} min, email={Email}",
            settings.FailedRecoveryEnabled,
            settings.FailedRecoveryIntervalMinutes,
            settings.EmailAlertEnabled);
        return (true, "Settings saved.");
    }

    public async Task CheckDownAlerts()
    {
        var settings = _settings;
        List<NexaProcess> snapshot;
        await _lock.WaitAsync();
        try
        {
            snapshot = _processes.ToList();
        }
        finally { _lock.Release(); }

        foreach (var p in snapshot)
        {
            var prev = _previousStatus.GetValueOrDefault(p.Name, p.Status);
            _previousStatus[p.Name] = p.Status;

            if (p.Status == ProcessStatus.Online)
            {
                _alerts.ClearDownState(p.Name);
                continue;
            }

            if (prev == ProcessStatus.Online && p.Status == ProcessStatus.Errored)
                await _alerts.SendProcessDownAsync(p, settings);
        }
    }

    public async Task<(bool success, string message, NexaProcess? process)> Start(StartOptions options)
    {
        await _lock.WaitAsync();
        try
        {
            var existing = _processes.FirstOrDefault(p => p.Name == options.Name);
            if (existing != null && existing.Status == ProcessStatus.Online)
                return (false, $"Process '{options.Name}' is already running.", null);

            var nexaProcess = existing ?? new NexaProcess
            {
                Id = _nextId++,
                Name = options.Name,
                AutoRestart = options.AutoRestart
            };

            ApplyDefinition(nexaProcess, options);
            if (string.IsNullOrWhiteSpace(nexaProcess.WorkingDirectory))
                nexaProcess.WorkingDirectory = Directory.GetCurrentDirectory();
            CancelPendingRestart(nexaProcess.Id);
            nexaProcess.DesiredStatus = ProcessStatus.Online;
            nexaProcess.Status = ProcessStatus.Starting;

            if (existing == null) _processes.Add(nexaProcess);

            var launchError = LaunchProcess(nexaProcess, options.Environment);
            if (launchError != null)
            {
                SetErrored(nexaProcess, $"Start failed: {launchError}");
                return (false, launchError, nexaProcess);
            }

            nexaProcess.Restarts = 0;
            nexaProcess.StatusReason = null;
            nexaProcess.StartedAt = DateTime.UtcNow;
            nexaProcess.Status = ProcessStatus.Online;
            _resourceBreachStreak.Remove(nexaProcess.Id);
            _lastCpuTime.Remove(nexaProcess.Id);
            _lastCpuSample.Remove(nexaProcess.Id);
            _alerts.ClearDownState(nexaProcess.Name);
            _previousStatus[nexaProcess.Name] = ProcessStatus.Online;
            RecordRunStart(nexaProcess.Name, nexaProcess.StartedAt);

            await Persist();
            await PersistHistory();
            return (true, $"Process '{options.Name}' started.", nexaProcess);
        }
        finally { _lock.Release(); }
    }

    public Task<(bool success, string message, NexaProcess? process)> StartExisting(string target) =>
        StartExisting(target, settleAfterStart: true);

    public async Task<(bool success, string message, NexaProcess? process)> StartExisting(string target, bool settleAfterStart)
    {
        await _lock.WaitAsync();
        NexaProcess? p;
        string? resolveError;
        try
        {
            p = ProcessTarget.TryResolve(_processes, target, out resolveError);
            if (p == null)
                return (false, resolveError!, null);
            if (p.Status == ProcessStatus.Online)
                return (false, $"{ProcessTarget.Display(p)} is already online.", p);
        }
        finally { _lock.Release(); }

        var result = await Start(ToStartOptions(p));
        if (result.success && settleAfterStart)
            await Task.Delay(ProcessDefaults.StartSettleMs);
        return result;
    }

    public async Task<(bool success, string message)> StartAllProcesses()
    {
        List<string> targets;
        await _lock.WaitAsync();
        try
        {
            targets = _processes
                .Where(p => p.Status is not (ProcessStatus.Online or ProcessStatus.Starting))
                .OrderBy(p => p.Id)
                .Select(p => p.Id.ToString())
                .ToList();
        }
        finally { _lock.Release(); }

        if (targets.Count == 0)
            return (true, "No stopped processes to start.");

        var succeeded = 0;
        var failed = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var settleAfterStart = i == targets.Count - 1;
            var (ok, message, _) = await StartExisting(targets[i], settleAfterStart);
            if (ok)
                succeeded++;
            else
            {
                failed++;
                _logger.LogWarning("Start All: failed '{Target}': {Message}", targets[i], message);
            }
        }

        return (true, $"Start All finished: {succeeded} succeeded, {failed} failed (of {targets.Count}).");
    }

    public async Task<(bool success, string message)> Stop(string target)
    {
        await _lock.WaitAsync();
        try
        {
            var p = ProcessTarget.TryResolve(_processes, target, out var resolveError);
            if (p == null) return (false, resolveError!);

            CancelPendingRestart(p.Id);
            p.DesiredStatus = ProcessStatus.Stopped;

            if (p.Status is not (ProcessStatus.Online or ProcessStatus.Starting or ProcessStatus.Stopping))
            {
                // Still persist DesiredStatus so boot restore will not resurrect a stopped app.
                await Persist();
                return (false, $"{ProcessTarget.Display(p)} is not running.");
            }

            p.Status = ProcessStatus.Stopping;
            await KillProcess(p, graceful: true);
            p.Status = ProcessStatus.Stopped;
            p.Pid = null;
            p.StatusReason = null;
            RecordRunEnd(p.Name, DateTime.UtcNow, exitCode: null, ProcessRunOutcome.Stopped);

            await Persist();
            await PersistHistory();
            return (true, $"{ProcessTarget.Display(p)} stopped.");
        }
        finally { _lock.Release(); }
    }

    public Task<(bool success, string message, NexaProcess? process)> Restart(string target) =>
        Restart(target, settleAfterStart: true);

    public async Task<(bool success, string message, NexaProcess? process)> Restart(string target, bool settleAfterStart)
    {
        await _lock.WaitAsync();
        NexaProcess? resolved;
        string? resolveError;
        try
        {
            resolved = ProcessTarget.TryResolve(_processes, target, out resolveError);
            if (resolved == null)
                return (false, resolveError!, null);
        }
        finally { _lock.Release(); }

        var name = resolved.Name;
        var processId = resolved.Id;
        var (stopOk, stopMsg) = await Stop(target);
        if (!stopOk && !stopMsg.EndsWith("is not running.", StringComparison.Ordinal))
            return (false, stopMsg, null);

        CancellationToken settleToken;
        await _lock.WaitAsync();
        try
        {
            // User-initiated restart should come back Online.
            var tracked = _processes.FirstOrDefault(x => x.Name == name);
            if (tracked != null)
                tracked.DesiredStatus = ProcessStatus.Online;
            settleToken = BeginPendingRestart(processId);
        }
        finally { _lock.Release(); }

        try
        {
            await Task.Delay(ProcessDefaults.RestartSettleMs, settleToken);
        }
        catch (OperationCanceledException)
        {
            return (false, $"{ProcessTarget.Display(resolved)} restart cancelled.", null);
        }

        await _lock.WaitAsync();
        NexaProcess? p;
        StartOptions? opts;
        try
        {
            p = _processes.FirstOrDefault(x => x.Name == name);
            if (p == null) return (false, $"{ProcessTarget.Display(resolved)} was removed.", null);
            if (p.DesiredStatus != ProcessStatus.Online)
                return (false, $"{ProcessTarget.Display(p)} restart cancelled.", null);
            opts = ToStartOptions(p);
            CancelPendingRestart(p.Id);
        }
        finally { _lock.Release(); }

        var result = await Start(opts);
        if (result.success && settleAfterStart)
            await Task.Delay(ProcessDefaults.StartSettleMs);
        return result;
    }

    public async Task<(bool success, string message)> RestartAllProcesses()
    {
        List<string> names;
        await _lock.WaitAsync();
        try
        {
            names = _processes.Select(p => p.Name).ToList();
        }
        finally { _lock.Release(); }

        if (names.Count == 0)
            return (false, "No processes to restart.");

        var succeeded = 0;
        var failed = 0;
        for (var i = 0; i < names.Count; i++)
        {
            var settleAfterStart = i == names.Count - 1;
            var (ok, message, _) = await Restart(names[i], settleAfterStart);
            if (ok)
                succeeded++;
            else
            {
                failed++;
                _logger.LogWarning("Restart All: failed '{Name}': {Message}", names[i], message);
            }
        }

        return (true, $"Restart All finished: {succeeded} succeeded, {failed} failed (of {names.Count}).");
    }

    public async Task<(bool success, string message)> Delete(string target)
    {
        await _lock.WaitAsync();
        try
        {
            var p = ProcessTarget.TryResolve(_processes, target, out var resolveError);
            if (p == null) return (false, resolveError!);

            CancelPendingRestart(p.Id);
            p.DesiredStatus = ProcessStatus.Stopped;

            if (p.Status == ProcessStatus.Online || p.Status == ProcessStatus.Starting)
            {
                await KillProcess(p, graceful: true);
                RecordRunEnd(p.Name, DateTime.UtcNow, exitCode: null, ProcessRunOutcome.Stopped);
            }

            _processes.Remove(p);
            await Persist();
            await PersistHistory();
            return (true, $"{ProcessTarget.Display(p)} deleted.");
        }
        finally { _lock.Release(); }
    }

    public async Task<(bool success, string message, int removed)> ClearAll()
    {
        await _lock.WaitAsync();
        try
        {
            var snapshot = _processes.ToList();
            if (snapshot.Count == 0)
                return (true, "Process list is already empty.", 0);

            foreach (var p in snapshot)
            {
                CancelPendingRestart(p.Id);
                p.DesiredStatus = ProcessStatus.Stopped;
                if (p.Status == ProcessStatus.Online || p.Status == ProcessStatus.Starting)
                {
                    await KillProcess(p, graceful: true);
                    RecordRunEnd(p.Name, DateTime.UtcNow, exitCode: null, ProcessRunOutcome.Stopped);
                }
            }

            foreach (var cts in _pendingRestarts.Values)
            {
                try { cts.Cancel(); } catch { /* ignore */ }
                cts.Dispose();
            }
            _pendingRestarts.Clear();

            foreach (var session in _logSessions.Values)
                session.Dispose();
            _logSessions.Clear();
            _runningProcesses.Clear();
            _lastCpuTime.Clear();
            _lastCpuSample.Clear();
            _resourceBreachStreak.Clear();
            _processes.Clear();
            _nextId = 0;

            await Persist();
            return (true, $"Cleared {snapshot.Count} process(es) and saved an empty list.", snapshot.Count);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<NexaProcess>> GetAll()
    {
        await _lock.WaitAsync();
        try { return [.. _processes]; }
        finally { _lock.Release(); }
    }

    public async Task<(bool success, string message)> ImportBatch(IReadOnlyList<StartOptions> options, bool start)
    {
        if (options.Count == 0)
            return (false, "No processes to import.");

        await _lock.WaitAsync();
        try
        {
            foreach (var opt in options)
            {
                var existing = _processes.FirstOrDefault(p => p.Name == opt.Name);
                if (existing != null)
                {
                    ApplyDefinition(existing, opt);
                    if (existing.Status != ProcessStatus.Online)
                    {
                        existing.Status = ProcessStatus.Stopped;
                        existing.Pid = null;
                    }
                }
                else
                {
                    var p = new NexaProcess
                    {
                        Id = _nextId++,
                        Name = opt.Name,
                        Status = ProcessStatus.Stopped,
                        DesiredStatus = ProcessStatus.Stopped,
                        AutoRestart = opt.AutoRestart
                    };
                    ApplyDefinition(p, opt);
                    _processes.Add(p);
                }

                var touched = _processes.First(x => x.Name == opt.Name);
                if (touched.Status != ProcessStatus.Online)
                    touched.DesiredStatus = start ? ProcessStatus.Online : ProcessStatus.Stopped;
            }

            await Persist();
        }
        finally { _lock.Release(); }

        if (!start)
            return (true, $"Imported {options.Count} process(es). Use Start in the tray or CLI to run them. Online apps keep running until restarted.");

        var started = 0;
        var failed = 0;
        for (var i = 0; i < options.Count; i++)
        {
            var (ok, _, _) = await Start(options[i]);
            if (ok) started++;
            else failed++;

            if (i < options.Count - 1)
                await Task.Delay(ProcessDefaults.StartSettleMs);
        }

        return (true, $"Imported {options.Count} process(es). Started {started}, failed {failed}.");
    }

    private static void ApplyDefinition(NexaProcess process, StartOptions options)
    {
        ApplyLogPaths(process, options);
        process.ExecutablePath = options.ExecutablePath;
        process.Arguments = options.Arguments;
        process.MaxCpuPercent = options.MaxCpuPercent;
        process.MaxMemoryMb = options.MaxMemoryMb;
        process.MaxRestartAttempts = options.MaxRestartAttempts > 0
            ? options.MaxRestartAttempts
            : ProcessDefaults.MaxRestartAttempts;
        process.LogTimestamps = options.LogTimestamps;
        process.Environment = options.Environment;
        process.AutoRestart = options.AutoRestart;
        process.WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? process.WorkingDirectory
            : options.WorkingDirectory;
        process.Url = ProcessUrlHelper.ResolveUrl(options.Url, options.Arguments, options.Environment);
    }

    public async Task<(bool success, string body, string? logPath)> GetLogs(string target, int lines = 50, LogStream stream = LogStream.Combined)
    {
        await _lock.WaitAsync();
        NexaProcess? p;
        string? resolveError;
        try { p = ProcessTarget.TryResolve(_processes, target, out resolveError); }
        finally { _lock.Release(); }

        if (p == null) return (false, resolveError!, null);

        var path = ResolveLogPathForRead(p, stream);
        if (!File.Exists(path)) return (true, "(no logs yet)", path);

        try
        {
            var text = await LogFileHelper.ReadTailAsync(path, lines);
            return (true, string.IsNullOrWhiteSpace(text) ? "(no logs yet)" : text, path);
        }
        catch (Exception ex)
        {
            return (false, $"Could not read log file '{path}': {ex.Message}", path);
        }
    }

    public async Task<(bool success, string message)> StopAll()
    {
        List<string> targets;
        await _lock.WaitAsync();
        try
        {
            targets = _processes
                .Where(p => p.Status is ProcessStatus.Online or ProcessStatus.Starting)
                .Select(p => p.Id.ToString())
                .ToList();
        }
        finally { _lock.Release(); }

        if (targets.Count == 0)
            return (true, "No running processes.");

        var stopped = 0;
        foreach (var t in targets)
        {
            var (ok, _) = await Stop(t);
            if (ok) stopped++;
        }

        return (true, $"Stopped {stopped} of {targets.Count} process(es).");
    }

    public async Task<(bool success, string message)> DeleteAll()
    {
        var (ok, message, _) = await ClearAll();
        return (ok, message);
    }

    public async Task<(bool success, string message)> Save()
    {
        await _lock.WaitAsync();
        try
        {
            await Persist();
            return (true, $"Saved process list to {NexaRunPaths.ProcessesFile}");
        }
        finally { _lock.Release(); }
    }

    public async Task<(bool success, string message)> Resurrect()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var p in _processes)
            {
                if (p.Status is ProcessStatus.Online or ProcessStatus.Errored or ProcessStatus.Starting)
                    p.DesiredStatus = ProcessStatus.Online;
            }

            await Persist();
        }
        finally { _lock.Release(); }

        await RestoreDesiredOnline();
        return (true, "Resurrected desired online processes.");
    }

    public async Task<(bool success, string message, NexaProcess? process)> Describe(string target)
    {
        await _lock.WaitAsync();
        try
        {
            var p = ProcessTarget.TryResolve(_processes, target, out var resolveError);
            if (p == null) return (false, resolveError!, null);
            return (true, string.Empty, p);
        }
        finally { _lock.Release(); }
    }

    public async Task<(bool success, string message)> ClearLogs(string target)
    {
        await _lock.WaitAsync();
        NexaProcess? p;
        string? resolveError;
        try { p = ProcessTarget.TryResolve(_processes, target, out resolveError); }
        finally { _lock.Release(); }

        if (p == null) return (false, resolveError!);

        try
        {
            if (_logSessions.TryGetValue(p.Id, out var session))
            {
                session.ClearAll();
            }
            else
            {
                foreach (var path in LogFileHelper.UniquePaths(p.LogFile, p.OutLogFile, p.ErrorLogFile))
                {
                    LogFileHelper.ClearFile(path);
                    LogFileHelper.DeleteRotated(path);
                }
            }

            return (true, $"Cleared logs for [{p.Id}] {p.Name}.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not clear logs for [{p.Id}] {p.Name}: {ex.Message}");
        }
    }

    private static void FixLegacyCombinedLogPath(NexaProcess p)
    {
        if (string.IsNullOrWhiteSpace(p.OutLogFile) || string.IsNullOrWhiteSpace(p.ErrorLogFile))
            return;

        if (!string.Equals(p.LogFile, p.OutLogFile, StringComparison.OrdinalIgnoreCase))
            return;

        var dir = Path.GetDirectoryName(p.OutLogFile);
        if (string.IsNullOrEmpty(dir)) return;

        p.LogFile = Path.Combine(dir, $"{p.Name}.log");
    }

    private static void ApplyLogPaths(NexaProcess process, StartOptions options)
    {
        process.OutLogFile = options.OutLogFile;
        process.ErrorLogFile = options.ErrorLogFile;
        process.LogTimestamps = options.LogTimestamps;

        if (!string.IsNullOrWhiteSpace(options.CombinedLogFile))
        {
            process.LogFile = options.CombinedLogFile;
        }
        else if (!string.IsNullOrWhiteSpace(options.OutLogFile) &&
                 !string.IsNullOrWhiteSpace(options.ErrorLogFile))
        {
            // Separate out/err files — never reuse out.log as combined (avoids double-open lock).
            var dir = Path.GetDirectoryName(options.OutLogFile)!;
            process.LogFile = Path.Combine(dir, $"{options.Name}.log");
        }
        else
        {
            process.LogFile = options.OutLogFile
                ?? options.ErrorLogFile
                ?? NexaRunPaths.DefaultProcessLogFile(options.Name);
        }

        foreach (var path in new[] { process.LogFile, process.OutLogFile, process.ErrorLogFile })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    private static string ResolveLogPathForRead(NexaProcess p, LogStream stream) =>
        stream switch
        {
            LogStream.Out => p.OutLogFile ?? p.LogFile,
            LogStream.Err => p.ErrorLogFile ?? p.LogFile,
            _ => p.LogFile
        };

    public async Task CheckAndRecoverFailed()
    {
        if (!_settings.FailedRecoveryEnabled)
            return;

        var intervalMs = _settings.FailedRecoveryIntervalMinutes
            * 60_000;
        var now = DateTime.UtcNow;
        if ((now - _lastFailedRecoveryUtc).TotalMilliseconds < intervalMs)
            return;

        _lastFailedRecoveryUtc = now;

        List<NexaProcess> failed;
        await _lock.WaitAsync();
        try
        {
            failed = _processes.Where(p => p.Status == ProcessStatus.Errored).ToList();
        }
        finally { _lock.Release(); }

        if (failed.Count == 0)
            return;

        _logger.LogInformation("Attempting recovery start for {Count} errored process(es)", failed.Count);

        foreach (var p in failed)
        {
            await _lock.WaitAsync();
            try
            {
                if (p.Status != ProcessStatus.Errored)
                    continue;
                p.Restarts = 0;
                WriteProcessEvent(p, "NEXARUN: Scheduled recovery — attempting start");
            }
            finally { _lock.Release(); }

            var (ok, message, _) = await Start(ToStartOptions(p));
            if (ok)
                _logger.LogInformation("Recovered errored process '{Name}'", p.Name);
            else
                _logger.LogWarning("Recovery start failed for '{Name}': {Message}", p.Name, message);
        }
    }

    public async Task CheckAndRestartCrashed()
    {
        await _lock.WaitAsync();
        List<NexaProcess> candidates;
        try
        {
            // Monitor every Online process — including AutoRestart=false — so exits are recorded.
            candidates = _processes
                .Where(p => p.Status == ProcessStatus.Online)
                .ToList();
        }
        finally { _lock.Release(); }

        foreach (var p in candidates)
        {
            int? exitCode;
            bool isRunning;
            await _lock.WaitAsync();
            try
            {
                isRunning = IsProcessRunning(p, out exitCode);
            }
            finally { _lock.Release(); }

            if (isRunning) continue;

            var memMb = p.MemoryUsage / 1_048_576.0;
            var exitText = exitCode.HasValue ? $"exit code {exitCode}" : "exit code unknown";
            var shouldAutoRestart = false;
            CancellationToken backoffToken = CancellationToken.None;
            var processId = p.Id;
            var processName = p.Name;

            await _lock.WaitAsync();
            try
            {
                // Re-check under lock in case Stop ran between samples.
                if (p.Status != ProcessStatus.Online)
                    continue;

                await KillProcess(p, graceful: false);
                p.Pid = null;

                var crashedAt = DateTime.UtcNow;
                RecordRunEnd(p.Name, crashedAt, exitCode, ProcessRunOutcome.Crashed);

                if (!p.AutoRestart)
                {
                    SetErrored(p,
                        $"Process exited ({exitText}; auto-restart off). " +
                        $"Last memory {memMb:F0} MB. Check {p.ErrorLogFile ?? p.LogFile} for app errors.");
                }
                else
                {
                    p.Restarts++;
                    if (p.Restarts > p.MaxRestartAttempts)
                    {
                        SetErrored(p,
                            $"Process exited ({exitText}; last memory {memMb:F0} MB per process, not a total). " +
                            $"Exceeded max restart attempts ({p.MaxRestartAttempts}). Check {p.ErrorLogFile ?? p.LogFile} for app errors.");
                    }
                    else
                    {
                        shouldAutoRestart = true;
                        backoffToken = BeginPendingRestart(p.Id);
                    }
                }
            }
            finally { _lock.Release(); }

            if (!shouldAutoRestart)
            {
                await Persist();
                await PersistHistory();
                continue;
            }

            _logger.LogWarning(
                "Process '{Name}' exited ({Exit}). Restarting ({Attempt}/{Max})...",
                processName, exitText, p.Restarts, p.MaxRestartAttempts);

            // Exponential backoff only (cap 30s) — do not stack RestartSettleMs.
            int delay = Math.Min((int)Math.Pow(2, Math.Min(p.Restarts - 1, 4)) * 1000, 30000);
            try
            {
                await Task.Delay(delay, backoffToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Crash restart cancelled for '{Name}'", processName);
                continue;
            }

            await _lock.WaitAsync();
            try
            {
                if (p.DesiredStatus != ProcessStatus.Online ||
                    p.Status is ProcessStatus.Stopped or ProcessStatus.Stopping ||
                    !_pendingRestarts.ContainsKey(processId))
                {
                    _logger.LogInformation("Skipping crash relaunch for '{Name}' (cancelled or no longer desired online)", processName);
                    CancelPendingRestart(processId);
                }
                else
                {
                    p.Status = ProcessStatus.Starting;
                    var restartError = LaunchProcess(p, p.Environment);
                    CancelPendingRestart(processId);
                    if (restartError == null)
                    {
                        p.StartedAt = DateTime.UtcNow;
                        p.StatusReason = null;
                        p.Status = ProcessStatus.Online;
                        _lastCpuTime.Remove(p.Id);
                        _lastCpuSample.Remove(p.Id);
                        RecordRunStart(p.Name, p.StartedAt);
                        _logger.LogInformation(
                            "Process '{Name}' restarted (attempt {Count}/{Max})",
                            p.Name, p.Restarts, p.MaxRestartAttempts);
                    }
                    else
                    {
                        SetErrored(p,
                            $"Process exited ({exitText}); restart failed: {restartError}. " +
                            $"Last memory {memMb:F0} MB (per-process limit {p.MaxMemoryMb?.ToString() ?? "none"} MB).");
                    }
                }
            }
            finally { _lock.Release(); }

            await Persist();
            await PersistHistory();
        }
    }

    private bool IsProcessRunning(NexaProcess p, out int? exitCode)
    {
        exitCode = null;

        if (_runningProcesses.TryGetValue(p.Id, out var osProc))
        {
            if (!osProc.HasExited)
                return true;

            try { exitCode = osProc.ExitCode; } catch { /* not available yet */ }
            return false;
        }

        if (!p.Pid.HasValue)
            return false;

        try
        {
            var attached = Process.GetProcessById(p.Pid.Value);
            if (!attached.HasExited)
            {
                _runningProcesses[p.Id] = attached;
                return true;
            }

            try { exitCode = attached.ExitCode; } catch { }
        }
        catch
        {
            /* PID no longer exists */
        }

        return false;
    }

    public async Task UpdateStats()
    {
        await _lock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var cpuCount = Environment.ProcessorCount;

            foreach (var p in _processes.Where(x => x.Status == ProcessStatus.Online))
            {
                if (!_runningProcesses.TryGetValue(p.Id, out var osProc) || osProc.HasExited) continue;
                try
                {
                    osProc.Refresh();
                    p.MemoryUsage = osProc.WorkingSet64;

                    var currentCpu = osProc.TotalProcessorTime;
                    if (_lastCpuTime.TryGetValue(p.Id, out var prevCpu) &&
                        _lastCpuSample.TryGetValue(p.Id, out var prevTime))
                    {
                        var elapsed = (now - prevTime).TotalSeconds;
                        if (elapsed > 0)
                            p.CpuUsage = (currentCpu - prevCpu).TotalSeconds / (elapsed * cpuCount) * 100.0;
                    }
                    _lastCpuTime[p.Id] = currentCpu;
                    _lastCpuSample[p.Id] = now;

                    if (p.Restarts > 0 &&
                        (now - p.StartedAt).TotalSeconds >= ProcessDefaults.MinUptimeSecondsToResetRestarts)
                    {
                        p.Restarts = 0;
                    }
                }
                catch { /* process may have exited between check and refresh */ }
            }
        }
        finally { _lock.Release(); }
    }

    public async Task CheckResourceLimits()
    {
        await _lock.WaitAsync();
        List<NexaProcess> candidates;
        try
        {
            candidates = _processes
                .Where(p => p.Status == ProcessStatus.Online && (p.MaxCpuPercent.HasValue || p.MaxMemoryMb.HasValue))
                .ToList();
        }
        finally { _lock.Release(); }

        foreach (var p in candidates)
        {
            string? limitReason = null;
            await _lock.WaitAsync();
            try
            {
                var memMb = p.MemoryUsage / 1_048_576.0;
                var cpuOver = p.MaxCpuPercent.HasValue && p.CpuUsage > p.MaxCpuPercent.Value;
                var memOver = p.MaxMemoryMb.HasValue && memMb > p.MaxMemoryMb.Value;

                if (!cpuOver && !memOver)
                {
                    _resourceBreachStreak.Remove(p.Id);
                    continue;
                }

                var streak = _resourceBreachStreak.TryGetValue(p.Id, out var n) ? n + 1 : 1;
                _resourceBreachStreak[p.Id] = streak;
                if (streak < ProcessDefaults.ResourceLimitBreachCount)
                    continue;

                _resourceBreachStreak.Remove(p.Id);

                limitReason = cpuOver
                    ? $"Per-process CPU limit exceeded: {p.CpuUsage:F1}% used (limit {p.MaxCpuPercent}% for '{p.Name}')"
                    : $"Per-process memory limit exceeded: {memMb:F0} MB used (limit {p.MaxMemoryMb} MB for '{p.Name}')";

                if (_logSessions.TryGetValue(p.Id, out var session))
                    session.WriteSystem($"NEXARUN: Restarting — {limitReason}");
            }
            finally { _lock.Release(); }

            if (limitReason == null) continue;

            _logger.LogWarning("Restarting '{Name}': {Reason}", p.Name, limitReason);

            var (ok, msg, restarted) = await Restart(p.Name);
            if (!ok || restarted?.Status == ProcessStatus.Errored)
            {
                await _lock.WaitAsync();
                try
                {
                    var proc = _processes.FirstOrDefault(x => x.Name == p.Name);
                    if (proc != null)
                    {
                        var detail = string.IsNullOrWhiteSpace(proc.StatusReason) ? msg : proc.StatusReason;
                        SetErrored(proc, $"{limitReason}. {detail}");
                    }
                }
                finally { _lock.Release(); }

                await Persist();
            }
            else if (restarted != null)
            {
                _logger.LogInformation("'{Name}' restarted due to resource limit", p.Name);
            }
        }
    }

    private static StartOptions ToStartOptions(NexaProcess p) => new()
    {
        Name = p.Name,
        ExecutablePath = p.ExecutablePath,
        Arguments = p.Arguments,
        WorkingDirectory = p.WorkingDirectory,
        AutoRestart = p.AutoRestart,
        MaxRestartAttempts = p.MaxRestartAttempts,
        MaxCpuPercent = p.MaxCpuPercent,
        MaxMemoryMb = p.MaxMemoryMb,
        OutLogFile = p.OutLogFile,
        ErrorLogFile = p.ErrorLogFile,
        CombinedLogFile = p.LogFile,
        LogTimestamps = p.LogTimestamps,
        Environment = p.Environment,
        Url = p.Url
    };

    private void SetErrored(NexaProcess p, string reason)
    {
        p.Status = ProcessStatus.Errored;
        p.Pid = null;
        p.StatusReason = reason;
        WriteProcessEvent(p, reason);
    }

    public async Task<(bool success, string message, List<ProcessRun> runs)> GetHistory(string target)
    {
        await _lock.WaitAsync();
        try
        {
            var p = ProcessTarget.TryResolve(_processes, target, out var resolveError);
            if (p == null)
                return (false, resolveError!, []);

            var cutoff = DateTime.UtcNow.AddDays(-7);
            var runs = _runHistory
                .Where(r => r.ProcessName == p.Name && r.StartedAt >= cutoff)
                .OrderByDescending(r => r.StartedAt)
                .ToList();
            return (true, string.Empty, runs);
        }
        finally { _lock.Release(); }
    }

    private void RecordRunStart(string name, DateTime startedAt)
    {
        _runHistory.Add(new ProcessRun
        {
            ProcessName = name,
            StartedAt = startedAt,
            Outcome = ProcessRunOutcome.Running
        });
        // Trim history older than 7 days
        var cutoff = DateTime.UtcNow.AddDays(-7);
        _runHistory.RemoveAll(r => r.StartedAt < cutoff);
    }

    private void RecordRunEnd(string name, DateTime endedAt, int? exitCode, ProcessRunOutcome outcome)
    {
        var run = _runHistory.LastOrDefault(r => r.ProcessName == name && r.Outcome == ProcessRunOutcome.Running);
        if (run != null)
        {
            run.EndedAt = endedAt;
            run.ExitCode = exitCode;
            run.Outcome = outcome;
        }
    }

    private async Task PersistHistory()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var toSave = _runHistory.Where(r => r.StartedAt >= cutoff).ToList();
            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_historyPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist run history");
        }
    }

    private string? LaunchProcess(NexaProcess p, Dictionary<string, string>? environment = null)
    {
        ProcessLogSession? logSession = null;
        try
        {
            logSession = new ProcessLogSession(
                p.LogFile,
                p.OutLogFile,
                p.ErrorLogFile,
                p.LogTimestamps);
            _logSessions[p.Id] = logSession;

            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = ProcessLaunchHelper.CreateStartInfo(
                    p.ExecutablePath,
                    p.Arguments,
                    p.WorkingDirectory,
                    redirectOutput: true,
                    environment);
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{p.ExecutablePath} {p.Arguments}\"",
                    WorkingDirectory = p.WorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                if (environment != null)
                {
                    foreach (var (key, value) in environment)
                        psi.Environment[key] = value;
                }
            }

            var osProc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            osProc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) logSession.WriteStdout(e.Data);
            };
            osProc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) logSession.WriteStderr(e.Data);
            };

            osProc.Start();
            osProc.BeginOutputReadLine();
            osProc.BeginErrorReadLine();

            p.Pid = osProc.Id;
            _runningProcesses[p.Id] = osProc;
            _lastCpuTime.Remove(p.Id);
            _lastCpuSample.Remove(p.Id);
            return null; // null = success
        }
        catch (Exception ex)
        {
            logSession?.Dispose();
            _logSessions.Remove(p.Id);

            var message = ex.Message;
            _logger.LogError(ex, "Failed to launch process '{Name}'", p.Name);
            WriteProcessEvent(p, $"NEXARUN: Launch failed — {message}");
            return message;
        }
    }

    private static string ResolveExecutable(string exe)
    {
        // Already absolute or relative path with extension — use as-is
        if (Path.IsPathRooted(exe) || exe.Contains(Path.DirectorySeparatorChar) || exe.Contains('/'))
            return exe;

        // Extensions to try on Windows when none given
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            // Try exact name first
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;

            // Then try appending each PATHEXT extension if the exe has no extension
            if (!Path.HasExtension(exe))
            {
                foreach (var ext in extensions)
                {
                    var withExt = full + ext;
                    if (File.Exists(withExt)) return withExt;
                }
            }
        }

        // Fall back — let the OS try; will throw a useful error if not found
        return exe;
    }

    private async Task KillProcess(NexaProcess p, bool graceful)
    {
        if (!_runningProcesses.TryGetValue(p.Id, out var osProc))
        {
            // Reattached or dictionary miss: try by saved PID.
            if (p.Pid is int pid)
            {
                try { osProc = Process.GetProcessById(pid); }
                catch { return; }
            }
            else
            {
                return;
            }
        }

        try
        {
            if (!osProc.HasExited)
            {
                if (graceful)
                    await WindowsProcessStopper.Stop(osProc, ProcessDefaults.StopTimeoutMs);
                else
                {
                    osProc.Kill(entireProcessTree: true);
                    await Task.WhenAny(osProc.WaitForExitAsync(), Task.Delay(5_000));
                }
            }
        }
        catch { /* already gone */ }
        finally
        {
            _runningProcesses.Remove(p.Id);
            _lastCpuTime.Remove(p.Id);
            _lastCpuSample.Remove(p.Id);
            _resourceBreachStreak.Remove(p.Id);
            if (_logSessions.TryGetValue(p.Id, out var session))
            {
                session.Dispose();
                _logSessions.Remove(p.Id);
            }
        }
    }

    private void WriteProcessEvent(NexaProcess p, string message)
    {
        var line = message.StartsWith("NEXARUN:", StringComparison.Ordinal) ? message : $"NEXARUN: {message}";

        if (_logSessions.TryGetValue(p.Id, out var session))
        {
            session.WriteSystem(line);
            return;
        }

        foreach (var path in new[] { p.ErrorLogFile, p.LogFile, p.OutLogFile, NexaRunPaths.DefaultProcessLogFile(p.Name) })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                LogFileHelper.AppendLine(path, message, p.LogTimestamps);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write event for '{Name}' to {Path}", p.Name, path);
            }
        }
    }

    private async Task Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_processes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_persistPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist process list");
        }
    }
}
