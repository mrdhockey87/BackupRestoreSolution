using BackupService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BackupRestoreService";
});

// Register BackupServiceCommunication as singleton AND hosted service
// This ensures it's created once and its StartAsync/StopAsync are called automatically
var communicationInstance = new BackupServiceCommunication();
builder.Services.AddSingleton(communicationInstance);
builder.Services.AddHostedService(sp => communicationInstance);

builder.Services.AddHostedService<BackupSchedulerService>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<BackupExecutor>();
builder.Services.AddSingleton<BackupProgressTracker>();

var host = builder.Build();
await host.RunAsync();


