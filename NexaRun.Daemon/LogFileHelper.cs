using System.Text;
using NexaRun.Shared;

namespace NexaRun.Daemon;

public static class LogFileHelper
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    public static void AppendLine(string path, string line, bool timestamps = true)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var text = timestamps
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {line}{Environment.NewLine}"
            : $"{line}{Environment.NewLine}";

        using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        var bytes = Utf8NoBom.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static async Task<string> ReadTailAsync(string path, int lineCount)
    {
        if (!File.Exists(path))
            return string.Empty;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
            lines.Add(line);

        return string.Join(Environment.NewLine, lines.TakeLast(lineCount));
    }

    public static void ClearFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite);
    }

    public static void DeleteRotated(string path, int generations = ProcessDefaults.MaxLogGenerations)
    {
        var max = Math.Max(1, generations);
        for (var i = 1; i <= max; i++)
        {
            var rotated = $"{path}.{i}";
            if (File.Exists(rotated))
                File.Delete(rotated);
        }
    }

    /// <summary>
    /// Rotate current → .1, .1 → .2, … dropping the oldest beyond <paramref name="generations"/>.
    /// </summary>
    public static void RotateGenerations(string path, int generations = ProcessDefaults.MaxLogGenerations)
    {
        try
        {
            if (!File.Exists(path)) return;

            var info = new FileInfo(path);
            if (info.Length < ProcessDefaults.MaxLogFileBytes) return;

            var max = Math.Max(1, generations);
            var oldest = $"{path}.{max}";
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (var i = max - 1; i >= 1; i--)
            {
                var src = $"{path}.{i}";
                var dst = $"{path}.{i + 1}";
                if (File.Exists(src))
                    File.Move(src, dst, overwrite: true);
            }

            File.Move(path, $"{path}.1", overwrite: true);
        }
        catch
        {
            /* rotation is best-effort */
        }
    }

    public static IEnumerable<string> UniquePaths(params string?[] paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                yield return full;
        }
    }
}
