# Uninstall-BackupService.ps1
# Uninstalls the BackupRestoreService Windows Service

# Requires Administrator privileges
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

Write-Host "Uninstalling BackupRestoreService..." -ForegroundColor Cyan

try {
    # Check if service exists
    $service = Get-Service -Name "BackupRestoreService" -ErrorAction SilentlyContinue
    
    if (-not $service) {
        Write-Host "Service is not installed." -ForegroundColor Yellow
        exit 0
    }
    
    Write-Host "Service found: $($service.DisplayName)" -ForegroundColor Gray
    Write-Host "Status: $($service.Status)" -ForegroundColor Gray
    
    # Stop the service if running
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..." -ForegroundColor Gray
        Stop-Service -Name "BackupRestoreService" -Force
        
        # Wait for service to stop
        $timeout = 30
        $elapsed = 0
        while ($service.Status -ne "Stopped" -and $elapsed -lt $timeout) {
            Start-Sleep -Seconds 1
            $service.Refresh()
            $elapsed++
        }
        
        if ($service.Status -eq "Stopped") {
            Write-Host "Service stopped." -ForegroundColor Green
        } else {
            Write-Host "WARNING: Service did not stop within $timeout seconds." -ForegroundColor Yellow
        }
    }
    
    # Delete the service
    Write-Host "Removing service..." -ForegroundColor Gray
    sc.exe delete BackupRestoreService
    
    Start-Sleep -Seconds 2
    
    # Verify removal
    $checkService = Get-Service -Name "BackupRestoreService" -ErrorAction SilentlyContinue
    if ($checkService) {
        Write-Host "WARNING: Service still exists after deletion attempt." -ForegroundColor Yellow
        Write-Host "You may need to restart Windows to complete removal." -ForegroundColor Yellow
    } else {
        Write-Host "`nSUCCESS! BackupRestoreService has been uninstalled." -ForegroundColor Green
    }
}
catch {
    Write-Host "`nERROR: Failed to uninstall service!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
