# Install-BackupService.ps1
# Installs the BackupRestoreService as a Windows Service

# Requires Administrator privileges
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

# Determine configuration (Debug or Release)
$configuration = "Debug"
if ($args.Count -gt 0) {
    $configuration = $args[0]
}

# Path to the service executable
$serviceExePath = Join-Path $PSScriptRoot "artifacts\bin\$configuration\BackupService.exe"

# Check if the executable exists
if (-not (Test-Path $serviceExePath)) {
    Write-Host "ERROR: Service executable not found at: $serviceExePath" -ForegroundColor Red
    Write-Host "Please build the solution first!" -ForegroundColor Yellow
    Write-Host "In Visual Studio: Build -> Rebuild Solution" -ForegroundColor Cyan
    exit 1
}

Write-Host "Installing BackupRestoreService..." -ForegroundColor Cyan
Write-Host "Service Executable: $serviceExePath" -ForegroundColor Gray

try {
    # Check if service already exists
    $existingService = Get-Service -Name "BackupRestoreService" -ErrorAction SilentlyContinue
    
    if ($existingService) {
        Write-Host "Service already exists! Uninstalling first..." -ForegroundColor Yellow
        
        # Stop the service if running
        if ($existingService.Status -eq "Running") {
            Write-Host "Stopping service..." -ForegroundColor Gray
            Stop-Service -Name "BackupRestoreService" -Force
            Start-Sleep -Seconds 2
        }
        
        # Delete the service
        Write-Host "Removing existing service..." -ForegroundColor Gray
        sc.exe delete BackupRestoreService
        Start-Sleep -Seconds 2
    }
    
    # Install the service
    Write-Host "Creating service..." -ForegroundColor Gray
    New-Service -Name "BackupRestoreService" `
                -BinaryPathName $serviceExePath `
                -DisplayName "Backup & Restore Service" `
                -Description "Manages scheduled backups and backup execution for the Backup & Restore Solution" `
                -StartupType Automatic
    
    Write-Host "Starting service..." -ForegroundColor Gray
    Start-Service -Name "BackupRestoreService"
    
    # Wait for service to start
    Start-Sleep -Seconds 3
    
    # Check service status
    $service = Get-Service -Name "BackupRestoreService"
    if ($service.Status -eq "Running") {
        Write-Host "`nSUCCESS! BackupRestoreService installed and started." -ForegroundColor Green
        Write-Host "Status: $($service.Status)" -ForegroundColor Green
        
        # Get version if possible
        Write-Host "`nYou can verify the installation in the Service Management window." -ForegroundColor Cyan
    } else {
        Write-Host "`nWARNING: Service installed but not running." -ForegroundColor Yellow
        Write-Host "Status: $($service.Status)" -ForegroundColor Yellow
        Write-Host "Check Event Viewer for errors." -ForegroundColor Gray
    }
}
catch {
    Write-Host "`nERROR: Failed to install service!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
