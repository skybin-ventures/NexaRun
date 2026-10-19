using NexaRun.Daemon;
using NexaRun.Shared;
using Serilog;

NexaRunPaths.EnsureDirectories();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(NexaRunPaths.DaemonLogFile, rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options => options.ServiceName = "NexaRunDaemon");
    builder.Services.AddSystemd();
    builder.Services.AddSerilog();
    builder.Services.AddSingleton<ProcessAlertService>();
    builder.Services.AddSingleton<ProcessManager>();
    builder.Services.AddHostedService<DaemonWorker>();
    builder.Services.AddHostedService<IpcServer>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Daemon terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
