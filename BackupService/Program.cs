using SecureServerBackupService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;

try
{
    // Set Normal process priority to prevent Efficiency mode
    // Only backup operations should run at lower priority (BelowNormal)
    try
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        File.AppendAllText(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BackupRestoreService", "startup.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process priority set to Normal{Environment.NewLine}");
    }
    catch (Exception prioEx)
    {
        File.AppendAllText(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BackupRestoreService", "startup.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Warning: Failed to set priority: {prioEx.Message}{Environment.NewLine}");
    }

    // Simple startup logging
    var logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "BackupRestoreService");
    Directory.CreateDirectory(logDir);
    var startupLog = Path.Combine(logDir, "startup.log");
    File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BackupService starting{Environment.NewLine}");

    // Set service description with current version
    try
    {
        SetServiceDescription();
        File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Service description updated{Environment.NewLine}");
    }
    catch (Exception ex)
    {
        File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Failed to set description: {ex.Message}{Environment.NewLine}");
    }

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "BackupRestoreService";
    });

    var communicationInstance = new BackupServiceCommunication();
    builder.Services.AddSingleton(communicationInstance);
    builder.Services.AddHostedService(sp => communicationInstance);

    builder.Services.AddHostedService<BackupSchedulerService>();
    builder.Services.AddSingleton<JobManager>();
    builder.Services.AddSingleton<BackupExecutor>();
    builder.Services.AddSingleton<BackupProgressTracker>();

    var host = builder.Build();
    
    File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Host built, starting RunAsync{Environment.NewLine}");
    await host.RunAsync();
    
    File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Host stopped normally{Environment.NewLine}");
}
catch (Exception ex)
{
    var errorLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "BackupRestoreService",
        "startup_error.log");
    
    try
    {
        File.AppendAllText(errorLog, 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex.Message}{Environment.NewLine}" +
            $"Stack: {ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
    }
    catch { }
    
    throw;
}

static void SetServiceDescription()
{
    try
    {
        // Get version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "Unknown";
        
        // Strip Git commit hash if present
        int plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
        {
            version = version.Substring(0, plusIndex);
        }
        
        // Set service description
        using (var sc = new ServiceController("BackupRestoreService"))
        {
            // Use WMI to set description
            var wmiPath = $"Win32_Service.Name='BackupRestoreService'";
            using (var service = new System.Management.ManagementObject(wmiPath))
            {
                service.Get();
                var description = $"Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version {version})";
                service["Description"] = description;
                service.Put();
            }
        }
    }
    catch
    {
        // Ignore errors - not critical
    }
}


