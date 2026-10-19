namespace NexaRun.Shared;

public enum StartTargetKind
{
    Existing,
    NewExecutable,
    Ambiguous
}

public static class StartTargetResolver
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".ts", ".exe", ".cmd", ".bat", ".ps1", ".py", ".dll"
    };

    /// <summary>
    /// Decide whether <paramref name="target"/> starts an existing saved process or registers a new executable.
    /// Saved id/name wins when it matches. Otherwise path-like / existing files become new executables.
    /// </summary>
    public static StartTargetKind Resolve(
        string target,
        bool hasNewAppOptions,
        IReadOnlyList<string> existingNames,
        Func<int, bool> idExists)
    {
        if (string.IsNullOrWhiteSpace(target))
            return StartTargetKind.Ambiguous;

        target = target.Trim();

        if (hasNewAppOptions)
            return StartTargetKind.NewExecutable;

        if (int.TryParse(target, out var id) && idExists(id))
            return StartTargetKind.Existing;

        if (existingNames.Any(n => n.Equals(target, StringComparison.Ordinal)))
            return StartTargetKind.Existing;

        if (LooksLikeExecutableOrScript(target))
            return StartTargetKind.NewExecutable;

        // Unknown name that is not a file/path — treat as missing saved process (caller errors).
        return StartTargetKind.Existing;
    }

    public static bool LooksLikeExecutableOrScript(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (target.Contains(Path.DirectorySeparatorChar) || target.Contains('/') || target.Contains('\\'))
            return true;

        var ext = Path.GetExtension(target);
        if (!string.IsNullOrEmpty(ext) && ScriptExtensions.Contains(ext))
            return true;

        try
        {
            if (File.Exists(target))
                return true;

            var full = Path.GetFullPath(target);
            if (File.Exists(full))
                return true;
        }
        catch
        {
            /* invalid path chars */
        }

        return false;
    }
}
