# Quick rebuild - stops service, builds, reinstalls service
# Run as Administrator

Write-Host "=== Quick Rebuild and Reinstall ===" -ForegroundColor Cyan

# 1. Stop service
Write-Host "`n1. Stopping service..." -ForegroundColor Yellow
try {
    sc.exe stop SecureServerBackupService | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "   Service stopped" -ForegroundColor Green
} catch {
    Write-Host "   Service not running" -ForegroundColor Gray
}

# 2. Delete service (to unlock file)
Write-Host "`n2. Removing service registration..." -ForegroundColor Yellow
try {
    sc.exe delete SecureServerBackupService | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "   Service removed" -ForegroundColor Green
} catch {
    Write-Host "   Service not installed" -ForegroundColor Gray
}

# 3. Clean and build
Write-Host "`n3. Building solution..." -ForegroundColor Yellow
dotnet clean
dotnet build -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host "   BUILD FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "   Build successful!" -ForegroundColor Green

# 4. Reinstall service
Write-Host "`n4. Reinstalling service..." -ForegroundColor Yellow
$exePath = Join-Path $PSScriptRoot "artifacts\bin\Debug\SecureServerBackupService.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "   ERROR: SecureServerBackupService.exe not found!" -ForegroundColor Red
    exit 1
}

# Get version from file
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrEmpty($version)) {
    $version = $versionInfo.FileVersion
}

sc.exe create SecureServerBackupService binPath= "$exePath" start= auto DisplayName= "Secure Server Backup Service"
if ($LASTEXITCODE -ne 0) {
    Write-Host "   SERVICE CREATION FAILED!" -ForegroundColor Red
    exit 1
}

# Set service description with version
$description = "Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version $version)"
sc.exe description SecureServerBackupService "$description"

Write-Host "   Service created with version $version!" -ForegroundColor Green

# 5. Start service
Write-Host "`n5. Starting service..." -ForegroundColor Yellow
sc.exe start SecureServerBackupService
Start-Sleep -Seconds 3
Write-Host "   Service started!" -ForegroundColor Green

# 6. Verify
Write-Host "`n6. Verification..." -ForegroundColor Yellow
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
Write-Host "   Installed Version: $version" -ForegroundColor Green

$pipes = [System.IO.Directory]::GetFiles("\\.\pipe\") | Select-String "BackupRestore"
if ($pipes) {
    Write-Host "   Named Pipe: $pipes" -ForegroundColor Green
} else {
    Write-Host "   WARNING: Named pipe not found" -ForegroundColor Yellow
}

Write-Host "`n=== Complete! ===" -ForegroundColor Cyan
Write-Host "Test with: .\Test-NamedPipe.ps1" -ForegroundColor White
