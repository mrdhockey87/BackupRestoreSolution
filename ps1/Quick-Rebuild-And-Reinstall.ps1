# Quick-Rebuild-And-Reinstall.ps1
# Stops service, rebuilds BackupService, reinstalls service with version 5.13.11.10
# Run as Administrator!

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Quick Rebuild and Reinstall Service" -ForegroundColor Cyan
Write-Host "Version 5.13.11.10 - Failed Backup Cleanup Fix" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check admin
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    pause
    exit 1
}

$solutionRoot = $PSScriptRoot
$serviceName = "SecureServerBackupService"

Write-Host "[1/5] Stopping service..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -eq 'Running') {
            Stop-Service -Name $serviceName -Force
            Write-Host "  ✓ Service stopped" -ForegroundColor Green
        } else {
            Write-Host "  ℹ Service already stopped" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ℹ Service not installed yet" -ForegroundColor Gray
    }
} catch {
    Write-Host "  ⚠ Could not stop service: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[2/5] Deleting service..." -ForegroundColor Yellow
try {
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "  ✓ Service deleted" -ForegroundColor Green
} catch {
    Write-Host "  ℹ Service was not installed" -ForegroundColor Gray
}

Write-Host ""
Write-Host "[3/5] Building BackupService (Release)..." -ForegroundColor Yellow
Write-Host "  Building..." -ForegroundColor Gray
$buildOutput = dotnet build "$solutionRoot\BackupService\BackupService.csproj" --configuration Release 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build succeeded!" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build FAILED!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Build output:" -ForegroundColor Red
    Write-Host $buildOutput -ForegroundColor Red
    pause
    exit 1
}

Write-Host ""
Write-Host "[4/5] Installing service..." -ForegroundColor Yellow
$exePath = "$solutionRoot\artifacts\bin\Release\SecureServerBackupService.exe"
if (Test-Path $exePath) {
    Write-Host "  EXE found: $exePath" -ForegroundColor Gray
    
    sc.exe create $serviceName binPath= "$exePath" start= auto DisplayName= "Secure Server Backup Service" | Out-Null
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Service installed successfully!" -ForegroundColor Green
        
        # Set description with version
        sc.exe description $serviceName "Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version 5.13.11.10)" | Out-Null
        Write-Host "  ✓ Service description set" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Service installation FAILED!" -ForegroundColor Red
        pause
        exit 1
    }
} else {
    Write-Host "  ✗ EXE not found: $exePath" -ForegroundColor Red
    pause
    exit 1
}

Write-Host ""
Write-Host "[5/5] Starting service..." -ForegroundColor Yellow
try {
    Start-Service -Name $serviceName
    Start-Sleep -Seconds 2
    
    $service = Get-Service -Name $serviceName
    if ($service.Status -eq 'Running') {
        Write-Host "  ✓ Service started successfully!" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Service status: $($service.Status)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ✗ Could not start service: $_" -ForegroundColor Red
    Write-Host "  Check Event Viewer for service startup errors" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "✓ COMPLETE!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Delete the corrupt backup file manually:" -ForegroundColor White
Write-Host "     X:\BackupApplications\WDrive\WDrive.ssb" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Run your incremental backup again" -ForegroundColor White
Write-Host ""
Write-Host "  3. Check activity logs for:" -ForegroundColor White
Write-Host "     - [CLEANUP] messages if it fails first time" -ForegroundColor Gray
Write-Host "     - 'Automatically switching from Incremental to Full backup'" -ForegroundColor Gray
Write-Host "     - Successful Full backup creation" -ForegroundColor Gray
Write-Host ""
Write-Host "Version 5.13.11.10 is now running!" -ForegroundColor Green
Write-Host ""
pause
