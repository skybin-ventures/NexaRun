namespace NexaRun.Daemon;

public class DaemonWorker(ProcessManager processManager, ILogger<DaemonWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NexaRun daemon starting");
        await processManager.Load();
        logger.LogInformation("Process list loaded");
        await processManager.RestoreDesiredOnline();
        logger.LogInformation("Desired online processes restored");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processManager.ReloadSettings();
                await processManager.UpdateStats();
                await processManager.CheckDownAlerts();
                await processManager.CheckAndRestartCrashed();
                await processManager.CheckResourceLimits();
                await processManager.CheckAndRecoverFailed();
                await processManager.CheckWatchAndCron();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitor loop error");
            }

            await Task.Delay(5000, stoppingToken);
        }

        logger.LogInformation("NexaRun daemon stopping");
    }
}
