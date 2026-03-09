# Force complete service refresh - ensures Windows loads new binary
# Run as Administrator

Write-Host "=== Force Service Refresh ===" -ForegroundColor Cyan

# 1. Stop service
Write-Host "`n1. Stopping service (if running)..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name BackupRestoreService -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Stop-Service -Name BackupRestoreService -Force
        Write-Host "   Service stopped" -ForegroundColor Green
    } else {
        Write-Host "   Service not running" -ForegroundColor Gray
    }
} catch {
    Write-Host "   Service not found or already stopped" -ForegroundColor Gray
}

Start-Sleep -Seconds 2

# 2. Delete service
Write-Host "`n2. Removing service registration..." -ForegroundColor Yellow
sc.exe delete BackupRestoreService
Start-Sleep -Seconds 2

# 3. Kill any remaining processes
Write-Host "`n3. Killing any remaining BackupService processes..." -ForegroundColor Yellow
$processes = Get-Process -Name BackupService -ErrorAction SilentlyContinue
if ($processes) {
    $processes | ForEach-Object {
        Write-Host "   Killing PID $($_.Id)" -ForegroundColor Yellow
        $_ | Stop-Process -Force
    }
    Start-Sleep -Seconds 2
    Write-Host "   Processes killed" -ForegroundColor Green
} else {
    Write-Host "   No BackupService processes running" -ForegroundColor Gray
}

# 4. Verify binary is unlocked
Write-Host "`n4. Verifying binary is not locked..." -ForegroundColor Yellow
$exePath = Join-Path $PSScriptRoot "artifacts\bin\Debug\BackupService.exe"

try {
    # Try to open file for write to ensure it's not locked
    $fs = [System.IO.File]::Open($exePath, 'Open', 'ReadWrite', 'None')
    $fs.Close()
    Write-Host "   Binary is unlocked ?" -ForegroundColor Green
} catch {
    Write-Host "   WARNING: Binary may still be locked: $_" -ForegroundColor Yellow
    Write-Host "   Waiting 5 seconds..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}

# 5. Reinstall service
Write-Host "`n5. Reinstalling service..." -ForegroundColor Yellow

if (-not (Test-Path $exePath)) {
    Write-Host "   ERROR: BackupService.exe not found at: $exePath" -ForegroundColor Red
    exit 1
}

# Get version
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrEmpty($version)) {
    $version = $versionInfo.FileVersion
}

Write-Host "   Installing version: $version" -ForegroundColor Cyan

sc.exe create BackupRestoreService binPath= "$exePath" start= auto DisplayName= "Backup Restore Service"

if ($LASTEXITCODE -ne 0) {
    Write-Host "   FAILED!" -ForegroundColor Red
    exit 1
}

# Set description with version
$description = "Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version $version)"
sc.exe description BackupRestoreService "$description"

Write-Host "   Service installed with version $version ?" -ForegroundColor Green

# 6. Start service
Write-Host "`n6. Starting service..." -ForegroundColor Yellow
sc.exe start BackupRestoreService

Start-Sleep -Seconds 4

# 7. Verify it's running
Write-Host "`n7. Verifying service status..." -ForegroundColor Yellow
$service = Get-Service -Name BackupRestoreService
Write-Host "   Status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Red' })

# 8. Check named pipe
Write-Host "`n8. Checking named pipe..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
$pipes = [System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -like "*BackupRestore*" }
if ($pipes) {
    Write-Host "   Named pipe exists ?" -ForegroundColor Green
} else {
    Write-Host "   WARNING: Named pipe not found!" -ForegroundColor Red
}

# 9. Test connection
Write-Host "`n9. Testing named pipe connection..." -ForegroundColor Yellow
.\Test-NamedPipe.ps1

Write-Host "`n=== Complete ===" -ForegroundColor Cyan
