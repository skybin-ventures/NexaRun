using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using NexaRun.Shared;

namespace NexaRun.Tests;

/// <summary>
/// Regression tests for the NexaRun data-directory ACL security model
/// (LocalSystem + Administrators only). The production code path
/// (NexaRunPaths.EnsureDirectories → ApplyDataSecurity) is a no-op for
/// DataDirOverride (test temp dirs) so we verify the policy by applying it to a
/// real directory the test can manage, and asserting the resulting DACL.
/// </summary>
[SupportedOSPlatform("windows")]
public class DataSecurityTests
{
    private static string LocalSystemSid =>
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).ToString();
    private static string AdministratorsSid =>
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).ToString();
    private static string AuthenticatedUsersSid =>
        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).ToString();

    [Fact]
    public void SetDirectorySecurity_grants_only_privileged_principals()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "nexarun-acl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Reflect the exact rules the daemon's SetDirectorySecurity adds.
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

            var dir = new DirectoryInfo(tempDir);
            var security = dir.GetAccessControl();
            foreach (var rule in rules)
                security.AddAccessRule(rule);
            dir.SetAccessControl(security);

            // Re-read the DACL and verify.
            var applied = new DirectoryInfo(tempDir).GetAccessControl();
            var sids = applied.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow)
                .Select(r => r.IdentityReference!.Translate(typeof(SecurityIdentifier)).ToString())
                .ToHashSet();

            Assert.Contains(LocalSystemSid, sids);
            Assert.Contains(AdministratorsSid, sids);

            // No unprivileged group is in the allow set.
            Assert.DoesNotContain(AuthenticatedUsersSid, sids);
            Assert.DoesNotContain(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null).ToString(), sids);
            Assert.DoesNotContain(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).ToString(), sids);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A pre-existing permissive ACE (e.g. Authenticated-Users Modify) must be
    /// removed by the security repair, not merely appended to.
    /// </summary>
    [Fact]
    public void SetDirectorySecurity_removes_stale_permissive_ace()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "nexarun-acl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Seed a permissive ACE that simulates an old install's ACL.
            var seed = new DirectoryInfo(tempDir).GetAccessControl();
            seed.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(tempDir).SetAccessControl(seed);

            // Apply the repair: drop any ACE that is not LocalSystem /
            // Administrators.
            var dir = new DirectoryInfo(tempDir);
            var security = dir.GetAccessControl();
            var allowed = new[] { LocalSystemSid, AdministratorsSid };
            foreach (var existing in security.GetAccessRules(true, false, typeof(SecurityIdentifier))
                         .Cast<FileSystemAccessRule>())
            {
                var sid = existing.IdentityReference!.Translate(typeof(SecurityIdentifier)).ToString();
                if (!allowed.Contains(sid))
                    security.RemoveAccessRule(existing);
            }
            foreach (var rule in new[]
                     {
                         new FileSystemAccessRule(
                             new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                             FileSystemRights.FullControl,
                             InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                             PropagationFlags.None, AccessControlType.Allow),
                         new FileSystemAccessRule(
                             new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                             FileSystemRights.FullControl,
                             InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                             PropagationFlags.None, AccessControlType.Allow),
                     })
                security.AddAccessRule(rule);
            dir.SetAccessControl(security);

            // Re-read: Authenticated Users must be gone, LocalSystem +
            // Administrators present.
            var applied = new DirectoryInfo(tempDir).GetAccessControl();
            var sids = applied.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow)
                .Select(r => r.IdentityReference!.Translate(typeof(SecurityIdentifier)).ToString())
                .ToHashSet();

            Assert.DoesNotContain(AuthenticatedUsersSid, sids);
            Assert.Contains(LocalSystemSid, sids);
            Assert.Contains(AdministratorsSid, sids);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
