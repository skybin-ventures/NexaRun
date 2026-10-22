using System.Net.Sockets;

namespace NexaRun.Shared;

/// <summary>
/// Optional readiness gate after Process.Start (TCP listen port and/or ready file).
/// </summary>
public static class ProcessReadyWaiter
{
    public const string CancelledMessage = "Ready check cancelled.";

    public static async Task<(bool ready, string? error)> Wait(
        int? readyPort,
        string? readyFile,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (readyPort is null or <= 0 && string.IsNullOrWhiteSpace(readyFile))
            return (true, null);

        var timeout = timeoutMs > 0 ? timeoutMs : ProcessDefaults.ReadyTimeoutMs;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                var portOk = readyPort is null or <= 0 || IsPortListening(readyPort.Value);
                var fileOk = string.IsNullOrWhiteSpace(readyFile) || File.Exists(readyFile);
                if (portOk && fileOk)
                    return (true, null);

                await Task.Delay(250, linked.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, CancelledMessage);
        }
        catch (OperationCanceledException)
        {
            // linked timeout
        }

        if (cancellationToken.IsCancellationRequested)
            return (false, CancelledMessage);

        var parts = new List<string>();
        if (readyPort is > 0)
            parts.Add($"port {readyPort}");
        if (!string.IsNullOrWhiteSpace(readyFile))
            parts.Add($"file '{readyFile}'");
        return (false, $"Ready check timed out after {timeout}ms ({string.Join(", ", parts)}).");
    }

    public static bool IsCancelledError(string? error) =>
        string.Equals(error, CancelledMessage, StringComparison.Ordinal);

    private static bool IsPortListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", port);
            return connect.Wait(TimeSpan.FromMilliseconds(200)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
