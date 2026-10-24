using System.Collections.Concurrent;
using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Daemon;

/// <summary>
/// Per-process directory watchers with debounce and default ignore globs; raises restart requests.
/// </summary>
public sealed class ProcessWatchService : IDisposable
{
    private static readonly string[] DefaultIgnoreDirectoryNames =
        ["node_modules", ".git", ".svn", ".hg", "bin", "obj", ".vs"];

    private readonly ConcurrentDictionary<int, WatchEntry> _entries = new();
    private readonly ConcurrentDictionary<int, DateTime> _pending = new();
    private readonly ILogger _logger;

    public ProcessWatchService(ILogger logger) => _logger = logger;

    public void Sync(IReadOnlyList<NexaProcess> processes)
    {
        var wanted = processes.Where(p => p.Watch).Select(p => p.Id).ToHashSet();

        foreach (var id in _entries.Keys.ToArray())
        {
            if (!wanted.Contains(id))
                Remove(id);
        }

        foreach (var p in processes.Where(x => x.Watch))
        {
            var path = ResolveWatchPath(p);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _logger.LogWarning(
                    "Watch enabled for '{Name}' but path is missing: {Path}",
                    p.Name, path ?? "(null)");
                Remove(p.Id);
                continue;
            }

            var ignores = BuildIgnorePaths(p);
            if (_entries.TryGetValue(p.Id, out var existing) &&
                string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase) &&
                existing.IgnorePaths.SetEquals(ignores))
                continue;

            Remove(p.Id);
            try
            {
                var ignoreSnapshot = ignores;
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                watcher.Changed += (_, e) => OnChange(p.Id, e.FullPath, ignoreSnapshot);
                watcher.Created += (_, e) => OnChange(p.Id, e.FullPath, ignoreSnapshot);
                watcher.Deleted += (_, e) => OnChange(p.Id, e.FullPath, ignoreSnapshot);
                watcher.Renamed += (_, e) => OnChange(p.Id, e.FullPath, ignoreSnapshot);

                _entries[p.Id] = new WatchEntry(path, watcher, ignoreSnapshot);
                _logger.LogInformation("Watching '{Name}' at {Path}", p.Name, path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start watch for '{Name}' at {Path}", p.Name, path);
            }
        }
    }

    public void ClearPending(int processId) => _pending.TryRemove(processId, out _);

    public List<int> DrainDueRestarts(DateTime utcNow)
    {
        var due = new List<int>();
        var debounce = TimeSpan.FromMilliseconds(ProcessDefaults.WatchDebounceMs);
        foreach (var (id, at) in _pending.ToArray())
        {
            if (utcNow - at < debounce) continue;
            if (_pending.TryRemove(id, out _))
                due.Add(id);
        }

        return due;
    }

    private void OnChange(int processId, string? fullPath, HashSet<string> ignorePaths)
    {
        if (ShouldIgnore(fullPath, ignorePaths))
            return;

        _pending[processId] = DateTime.UtcNow;
    }

    public static bool ShouldIgnore(string? fullPath, HashSet<string> ignorePaths)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        try
        {
            var full = Path.GetFullPath(fullPath);
            foreach (var ignore in ignorePaths)
            {
                if (full.Equals(ignore, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (full.StartsWith(ignore.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(ignore.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var name = Path.GetFileName(full);
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                return true;

            var segments = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var segment in segments)
            {
                foreach (var dir in DefaultIgnoreDirectoryNames)
                {
                    if (segment.Equals(dir, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static HashSet<string> BuildIgnorePaths(NexaProcess p)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { p.LogFile, p.OutLogFile, p.ErrorLogFile })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var full = Path.GetFullPath(path);
                AddLogFileIgnores(set, full);
            }
            catch { /* ignore bad paths */ }
        }

        return set;
    }

    /// <summary>
    /// Adds a configured log file and its rotated generations (.1 … .N) to the
    /// ignore set without excluding the log file's parent directory. Excluding
    /// the parent would suppress the whole watched tree when a log sits
    /// directly under the watch root. Rotated files matter because
    /// <c>LogFileHelper.RotateGenerations</c> renames the live log to
    /// <c>.1</c> on rotation; without ignoring those, a rotation would trigger
    /// a restart loop. The generic <c>.log</c> rule in <see cref="ShouldIgnore"/>
    /// already covers the live file, so these additions are a belt-and-suspenders
    /// guarantee plus coverage for rotated names.
    /// </summary>
    private static void AddLogFileIgnores(HashSet<string> set, string full)
    {
        set.Add(full);
        for (var i = 1; i <= ProcessDefaults.MaxLogGenerations; i++)
            set.Add($"{full}.{i}");
    }

    private void Remove(int id)
    {
        if (_entries.TryRemove(id, out var entry))
        {
            try
            {
                entry.Watcher.EnableRaisingEvents = false;
                entry.Watcher.Dispose();
            }
            catch { /* ignore */ }
        }

        _pending.TryRemove(id, out _);
    }

    public static string? ResolveWatchPath(NexaProcess p)
    {
        // An absolute WatchPath is used as-is.
        if (!string.IsNullOrWhiteSpace(p.WatchPath))
        {
            if (Path.IsPathRooted(p.WatchPath))
                return Path.GetFullPath(p.WatchPath);

            // A relative WatchPath resolves against the process WorkingDirectory,
            // never the daemon's current directory. With a blank working
            // directory the relative path cannot be resolved to a stable
            // location, so the watch is skipped (Sync logs a warning).
            if (string.IsNullOrWhiteSpace(p.WorkingDirectory))
                return null;
            return Path.GetFullPath(Path.Combine(p.WorkingDirectory, p.WatchPath));
        }

        // No WatchPath set: watch the WorkingDirectory when present, else skip.
        if (!string.IsNullOrWhiteSpace(p.WorkingDirectory))
            return Path.GetFullPath(p.WorkingDirectory);

        return null;
    }

    public void Dispose()
    {
        foreach (var id in _entries.Keys.ToArray())
            Remove(id);
    }

    private sealed record WatchEntry(string Path, FileSystemWatcher Watcher, HashSet<string> IgnorePaths);
}
