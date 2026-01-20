using BackupService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BackupRestoreService";
});

builder.Services.AddHostedService<BackupSchedulerService>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<BackupExecutor>();

var host = builder.Build();
await host.RunAsync();
