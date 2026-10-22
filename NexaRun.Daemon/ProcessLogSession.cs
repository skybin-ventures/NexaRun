using NexaRun.Shared;
using System.Text;

namespace NexaRun.Daemon;

public sealed class ProcessLogSession : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _writeLock = new();
    private readonly bool _timestamps;
    private readonly Dictionary<string, StreamWriter> _writersByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _combinedPath;
    private readonly string? _outPath;
    private readonly string? _errPath;

    public ProcessLogSession(
        string? combinedPath,
        string? outPath,
        string? errPath,
        bool timestamps)
    {
        _timestamps = timestamps;
        _combinedPath = NormalizePath(combinedPath);
        _outPath = NormalizePath(outPath);
        _errPath = NormalizePath(errPath);

        TryOpen(_combinedPath);
        TryOpen(_outPath);
        TryOpen(_errPath);
    }

    public void WriteStdout(string line)
    {
        WriteToPath(_combinedPath, line);
        if (!PathsEqual(_combinedPath, _outPath))
            WriteToPath(_outPath, line);
    }

    public void WriteStderr(string line)
    {
        WriteToPath(_combinedPath, $"ERR {line}");
        if (!PathsEqual(_errPath, _combinedPath))
            WriteToPath(_errPath, line);
    }

    public void WriteSystem(string line) => WriteToPath(_combinedPath, line);

    public void ClearAll()
    {
        lock (_writeLock)
        {
            foreach (var writer in _writersByPath.Values)
                writer.Dispose();
            _writersByPath.Clear();

            foreach (var path in LogFileHelper.UniquePaths(_combinedPath, _outPath, _errPath))
            {
                LogFileHelper.ClearFile(path);
                LogFileHelper.DeleteRotated(path);
                TryOpen(path);
            }
        }
    }

    private void WriteToPath(string? path, string line)
    {
        if (path == null) return;

        lock (_writeLock)
        {
            if (!_writersByPath.TryGetValue(path, out var writer)) return;

            try
            {
                var text = _timestamps ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {line}" : line;
                writer.WriteLine(text);
            }
            catch (IOException)
            {
                // Avoid crashing output handlers if the file is temporarily locked.
            }
        }
    }

    private void TryOpen(string? path)
    {
        if (path == null || _writersByPath.ContainsKey(path)) return;

        try
        {
            _writersByPath[path] = OpenWriter(path);
        }
        catch (Exception ex)
        {
            throw new IOException($"Cannot open log file '{path}': {ex.Message}", ex);
        }
    }

    private static StreamWriter OpenWriter(string path)
    {
        LogFileHelper.RotateGenerations(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static bool PathsEqual(string? a, string? b) =>
        a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_writeLock)
        {
            foreach (var w in _writersByPath.Values)
                w.Dispose();
            _writersByPath.Clear();
        }
    }
}
