using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NexaRun.Shared;

public static class NexaRunPaths
{
    /// <summary>Test override for the data directory. When set, all path properties use this root.</summary>
    public static string? DataDirOverride { get; set; }

    public static string DataDir =>
        DataDirOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NexaRun");

    public static string LogDir => Path.Combine(DataDir, "logs");

    public static string ProcessesFile => Path.Combine(DataDir, "processes.json");

    public static string HistoryFile => Path.Combine(DataDir, "history.json");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string DaemonLogFile => Path.Combine(LogDir, "nexarun-daemon.log");

    public static string DefaultProcessLogFile(string processName) =>
        Path.Combine(LogDir, $"{processName}.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        TryGrantAuthenticatedUsersModify(DataDir);
        TryGrantAuthenticatedUsersModify(LogDir);
        MigrateLegacyData();
    }

    private static void TryGrantAuthenticatedUsersModify(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        if (!string.IsNullOrEmpty(DataDirOverride))
            return;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            var security = dirInfo.GetAccessControl();
            var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        catch
        {
            /* non-admin callers may not change ACLs — ignore */
        }
    }

    /// <summary>
    /// Copies data from legacy locations into ProgramData when the new folder has no processes.json yet:
    /// user %APPDATA%\NexaRun, LocalSystem profile AppData, and %USERPROFILE%\.nexarun.
    /// </summary>
    public static void MigrateLegacyData()
    {
        if (File.Exists(ProcessesFile))
        {
            MigrateLegacyDotNexaRunLogsOnly();
            return;
        }

        foreach (var legacyDir in EnumerateLegacyDataDirs())
        {
            if (!Directory.Exists(legacyDir)) continue;
            if (!File.Exists(Path.Combine(legacyDir, "processes.json"))) continue;

            CopyLegacyTree(legacyDir, DataDir);
            return;
        }

        MigrateLegacyDotNexaRunLogsOnly();
    }

    private static IEnumerable<string> EnumerateLegacyDataDirs()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NexaRun");

        // LocalSystem profile when the service previously used ApplicationData
        var systemProfile = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(systemProfile))
        {
            var windows = Path.GetDirectoryName(systemProfile); // ...\Windows\System32 → ...\Windows
            if (!string.IsNullOrEmpty(windows))
            {
                yield return Path.Combine(
                    windows, "System32", "config", "systemprofile", "AppData", "Roaming", "NexaRun");
            }
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nexarun");
    }

    private static void MigrateLegacyDotNexaRunLogsOnly()
    {
        var legacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nexarun");

        if (!Directory.Exists(legacyDir)) return;

        foreach (var file in new[] { "processes.json", "history.json", "settings.json" })
        {
            var legacyPath = Path.Combine(legacyDir, file);
            var newPath = Path.Combine(DataDir, file);
            if (File.Exists(legacyPath) && !File.Exists(newPath))
                File.Copy(legacyPath, newPath);
        }

        CopyLegacyLogs(Path.Combine(legacyDir, "logs"), LogDir);
    }

    private static void CopyLegacyTree(string legacyDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in new[] { "processes.json", "history.json", "settings.json" })
        {
            var src = Path.Combine(legacyDir, file);
            var dest = Path.Combine(destDir, file);
            if (File.Exists(src) && !File.Exists(dest))
                File.Copy(src, dest);
        }

        CopyLegacyLogs(Path.Combine(legacyDir, "logs"), Path.Combine(destDir, "logs"));
    }

    private static void CopyLegacyLogs(string legacyLogs, string destLogs)
    {
        if (!Directory.Exists(legacyLogs)) return;
        Directory.CreateDirectory(destLogs);
        foreach (var logFile in Directory.GetFiles(legacyLogs))
        {
            var dest = Path.Combine(destLogs, Path.GetFileName(logFile));
            if (!File.Exists(dest))
                File.Copy(logFile, dest);
        }
    }
}
