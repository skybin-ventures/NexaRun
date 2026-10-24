using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

    /// <summary>
    /// Ensures the data root and log directory exist and carries the secured
    /// ACL. Safe to call from any context (daemon, tray, CLI, settings store):
    /// it never throws. When called by a non-owner (the tray or CLI running as
    /// a regular user) the ACL cannot be set — that is expected and silent; the
    /// authoritative repair happens on the daemon (LocalSystem) and in the
    /// installer. See <see cref="ApplyDataSecurity"/>.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        if (System.OperatingSystem.IsWindows())
            ApplyDataSecurity();
        MigrateLegacyData();
    }

    /// <summary>
    /// Whether this process can authoritatively set the data-directory ACL —
    /// i.e. it runs as LocalSystem or an administrator (the contexts where the
    /// repair is meaningful). Tray/CLI as a plain user return false.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool CanApplyDataSecurity()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return id.Groups!.Any(g => g.Value == WellKnownSidType.LocalSystemSid.ToString()
                                       || g.Value == WellKnownSidType.BuiltinAdministratorsSid.ToString());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies the NexaRun data-directory security model to the data root and
    /// its log directory. Callers that can manage ACLs (LocalSystem / admin —
    /// the daemon and the installer) are expected to invoke this; when it
    /// cannot be applied the method logs an actionable warning instead of
    /// throwing, so a startup is never blocked, but the failure is surfaced.
    ///
    /// The model is deliberately narrow: LocalSystem (service) and the local
    /// Administrators group get full control so they can read and write the
    /// saved process definitions, settings, and application logs. No broader
    /// group (Authenticated Users, Users, Everybody) is granted access — an
    /// unprivileged local user must not be able to read secrets or tamper with
    /// definitions that a privileged daemon later launches. This aligns with
    /// the System Admin operating model in docs/SERVICE-ACCOUNT.md.
    ///
    /// For the optional dedicated-service-account workflow, an administrator
    /// also grants that account Modify on the data root — documented, not
    /// auto-detected.
    /// </summary>
    /// <returns>true when the DACL was written successfully.</returns>
    [SupportedOSPlatform("windows")]
    public static bool ApplyDataSecurity()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;
        if (string.IsNullOrEmpty(DataDirOverride))
            return true; // test override — do not touch test temp dirs

        var ok = true;
        foreach (var path in new[] { DataDir, LogDir })
        {
            if (SetDirectorySecurity(path))
                continue;

            ok = false;
            // Surface the failure so an operator notices the data root may be
            // left writable by a prior permissive ACL. The daemon logs to its
            // Serilog file; the tray/CLI have no dedicated log here, so a
            // best-effort EventLog write keeps it visible.
            LogDataSecurityWarning(path);
        }

        return ok;
    }

    [SupportedOSPlatform("windows")]
    private static bool SetDirectorySecurity(string path)
    {
        var dir = new DirectoryInfo(path);
        var security = dir.GetAccessControl();

        // Start from a known-good DACL: clear any pre-existing permissive ACEs
        // (e.g. the previous Authenticated-Users Modify grant) that would
        // otherwise keep applying to existing children.
        // Use SetAccessRule over each existing ACE with a Deny-type replacement
        // and then rebuild: the portable .NET DirectorySecurity has no
        // ResetAccessRules, so enumerate the current rules and drop any ACE
        // that is not one of our two allowed principals.
        var allowedSids = new[] {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).ToString(),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).ToString(),
        };

        var currentRules = security.GetAccessRules(true, false, typeof(NTAccount))
            .Cast<FileSystemAccessRule>()
            .ToList();
        foreach (var existing in currentRules)
        {
            var sid = existing.IdentityReference.Translate(typeof(SecurityIdentifier)).ToString();
            if (!allowedSids.Contains(sid))
                security.RemoveAccessRule(existing);
        }

        var rules = new List<FileSystemAccessRule>
        {
            new(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow),
            new(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow),
        };

        foreach (var rule in rules)
            security.AddAccessRule(rule);

        try
        {
            dir.SetAccessControl(security);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void LogDataSecurityWarning(string path)
    {
        if (!System.OperatingSystem.IsWindows())
            return;

        const string source = "NexaRun-Tray";
        var message =
            $"NexaRun could not apply the data-directory security model to '{path}'. " +
            "The data root may remain writable by a previously permissive ACL. " +
            "Run the installer (which applies the ACL as administrator) or set it manually " +
            "so that only LocalSystem and the Administrators group have access to " +
            "%ProgramData%\\NexaRun.";

        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(source))
            {
                // Best effort — creating a source requires admin; ignore if we
                // cannot.
                try { System.Diagnostics.EventLog.CreateEventSource(source, "Application"); }
                catch { /* ignore */ }
            }

            if (System.Diagnostics.EventLog.SourceExists(source))
                System.Diagnostics.EventLog.WriteEntry(source, message,
                    System.Diagnostics.EventLogEntryType.Warning);
        }
        catch
        {
            /* no EventLog access in this context — already logged to the daemon file log where applicable */
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
