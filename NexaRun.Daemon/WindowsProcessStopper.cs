using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexaRun.Daemon;

/// <summary>
/// Best-effort soft stop on Windows, then force-kill the process tree.
/// Windowless redirected children usually have no console, so CTRL_BREAK often fails —
/// force kill remains the reliable backstop.
/// </summary>
internal static class WindowsProcessStopper
{
    public static async Task Stop(Process osProc, int timeoutMs, CancellationToken ct = default)
    {
        if (osProc.HasExited)
            return;

        TrySoftSignal(osProc);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeoutMs);
            await osProc.WaitForExitAsync(linked.Token);
            return;
        }
        catch (OperationCanceledException)
        {
            /* timeout or caller cancel — fall through to force kill */
        }

        if (!osProc.HasExited)
        {
            try
            {
                osProc.Kill(entireProcessTree: true);
                await Task.WhenAny(osProc.WaitForExitAsync(), Task.Delay(5_000, ct));
            }
            catch
            {
                /* already gone */
            }
        }
    }

    private static void TrySoftSignal(Process osProc)
    {
        try
        {
            osProc.Refresh();
            if (osProc.MainWindowHandle != IntPtr.Zero)
            {
                osProc.CloseMainWindow();
                return;
            }
        }
        catch
        {
            /* ignore */
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // CTRL_BREAK to the child's console group when one exists.
            if (NativeMethods.AttachConsole((uint)osProc.Id))
            {
                NativeMethods.SetConsoleCtrlHandler(null, true);
                try
                {
                    NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_BREAK_EVENT, 0);
                }
                finally
                {
                    NativeMethods.FreeConsole();
                    NativeMethods.SetConsoleCtrlHandler(null, false);
                }
            }
        }
        catch
        {
            /* common for CreateNoWindow / redirected children */
        }
    }

    private static class NativeMethods
    {
        public const uint CTRL_BREAK_EVENT = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        public delegate bool ConsoleCtrlDelegate(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);
    }
}
