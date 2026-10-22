using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexaRun.Daemon;

/// <summary>
/// Best-effort Windows Job Object: groups a process tree for explicit terminate-on-stop.
/// Does not use KILL_ON_JOB_CLOSE so children can survive a daemon recycle for PID reattach.
/// </summary>
internal sealed class WindowsJobObject : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private WindowsJobObject(IntPtr handle) => _handle = handle;

    public static WindowsJobObject? TryCreate(string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var handle = CreateJobObject(IntPtr.Zero, name);
            if (handle == IntPtr.Zero)
                return null;

            return new WindowsJobObject(handle);
        }
        catch
        {
            return null;
        }
    }

    public bool TryAssign(Process process)
    {
        if (_disposed || _handle == IntPtr.Zero || process.HasExited)
            return false;

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Terminate all processes currently in the job (intentional stop/clear).</summary>
    public void Terminate(uint exitCode = 1)
    {
        if (_disposed || _handle == IntPtr.Zero)
            return;

        try
        {
            TerminateJobObject(_handle, exitCode);
        }
        catch
        {
            /* best-effort */
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
