using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NexaRun.Shared;

public static class ProcessLaunchHelper
{
    public static ProcessStartInfo CreateStartInfo(
        string executable,
        string arguments,
        string workingDirectory,
        bool redirectOutput,
        IDictionary<string, string>? environment = null)
    {
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : workingDirectory;

        var pathOverlay = TryGetPathOverlay(environment);
        var (fileName, mergedArgs) = ResolveLaunchTarget(executable, arguments, cwd, pathOverlay);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = mergedArgs,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (redirectOutput)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        if (environment != null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        return psi;
    }

    /// <summary>
    /// Resolves an executable on PATH (PATHEXT). Avoids cmd.exe for .exe/.com so child processes stay windowless.
    /// When <paramref name="pathOverlay"/> is set (e.g. from process <c>PATH</c> env), that value is searched
    /// instead of the process environment PATH — so LocalSystem can find tools via per-app <c>--env PATH=...</c>.
    /// </summary>
    public static string ResolveExecutable(
        string executable,
        string workingDirectory,
        string? pathOverlay = null)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return executable;

        if (Path.IsPathRooted(executable))
        {
            if (File.Exists(executable)) return executable;
            return executable;
        }

        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains('/'))
        {
            var combined = Path.GetFullPath(Path.Combine(workingDirectory, executable));
            if (File.Exists(combined)) return combined;
        }

        var pathEnv = !string.IsNullOrWhiteSpace(pathOverlay)
            ? pathOverlay
            : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        var extensions = pathext
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hasExt = Path.HasExtension(executable);
        var namesToTry = hasExt
            ? new[] { executable }
            : extensions.Select(ext => executable + ext).Prepend(executable).ToArray();

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0) continue;

            foreach (var name in namesToTry)
            {
                var candidate = Path.Combine(trimmed, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return executable;
    }

    /// <summary>
    /// Maps script paths to an interpreter + args (PM2-style). Already-resolved interpreters
    /// (node, python, etc.) are left unchanged. Only wraps known script extensions.
    /// </summary>
    public static (string FileName, string Arguments) ResolveLaunchTarget(
        string executable,
        string arguments,
        string workingDirectory,
        string? pathOverlay = null)
    {
        var resolved = ResolveExecutable(executable, workingDirectory, pathOverlay);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (resolved, arguments);

        if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = ResolveExecutable("cmd.exe", workingDirectory, pathOverlay);
            var inner = string.IsNullOrWhiteSpace(arguments)
                ? QuoteIfNeeded(resolved)
                : $"{QuoteIfNeeded(resolved)} {arguments}";
            return (cmd, $"/c {inner}");
        }

        var ext = Path.GetExtension(resolved);
        if (string.IsNullOrEmpty(ext))
            return (resolved, arguments);

        if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".cjs", StringComparison.OrdinalIgnoreCase))
        {
            return WrapWithInterpreter("node", resolved, arguments, workingDirectory, pathOverlay);
        }

        if (ext.Equals(".py", StringComparison.OrdinalIgnoreCase))
            return WrapWithInterpreter("python", resolved, arguments, workingDirectory, pathOverlay);

        if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var shell = ResolveFirstOnPath(workingDirectory, pathOverlay, "pwsh.exe", "pwsh", "powershell.exe", "powershell");
            var psArgs = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteIfNeeded(resolved)}";
            if (!string.IsNullOrWhiteSpace(arguments))
                psArgs = $"{psArgs} {arguments}";
            return (shell, psArgs);
        }

        if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return WrapWithInterpreter("dotnet", resolved, arguments, workingDirectory, pathOverlay);

        return (resolved, arguments);
    }

    private static (string FileName, string Arguments) WrapWithInterpreter(
        string interpreter,
        string scriptPath,
        string arguments,
        string workingDirectory,
        string? pathOverlay)
    {
        var exe = ResolveExecutable(interpreter, workingDirectory, pathOverlay);
        var merged = string.IsNullOrWhiteSpace(arguments)
            ? QuoteIfNeeded(scriptPath)
            : $"{QuoteIfNeeded(scriptPath)} {arguments}";
        return (exe, merged);
    }

    private static string ResolveFirstOnPath(string workingDirectory, string? pathOverlay, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            var resolved = ResolveExecutable(name, workingDirectory, pathOverlay);
            if (File.Exists(resolved))
                return resolved;
        }

        return ResolveExecutable(candidates[^1], workingDirectory, pathOverlay);
    }

    private static string? TryGetPathOverlay(IDictionary<string, string>? environment)
    {
        if (environment == null) return null;
        foreach (var (key, value) in environment)
        {
            if (key.Equals("PATH", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>CreateProcess-safe quoting: wrap when needed; double embedded quotes.</summary>
    public static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var needsQuotes = false;
        foreach (var ch in value)
        {
            if (ch is ' ' or '\t' or '"')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
