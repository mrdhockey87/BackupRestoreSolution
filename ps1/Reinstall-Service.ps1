# Reinstall BackupRestoreService with latest build
# Run as Administrator

Write-Host "=== BackupRestoreService Reinstallation ===" -ForegroundColor Cyan

# Stop and remove old service
Write-Host "`n1. Stopping existing service..." -ForegroundColor Yellow
try {
    sc.exe stop BackupRestoreService | Out-Null
    Start-Sleep -Seconds 2
} catch {
    Write-Host "   Service not running (OK)" -ForegroundColor Gray
}

Write-Host "2. Removing existing service..." -ForegroundColor Yellow
try {
    sc.exe delete BackupRestoreService | Out-Null
    Start-Sleep -Seconds 2
} catch {
    Write-Host "   Service not installed (OK)" -ForegroundColor Gray
}

# Build latest version
Write-Host "`n3. Building latest version..." -ForegroundColor Yellow
dotnet build BackupService\BackupService.csproj -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host "   BUILD FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "   Build successful!" -ForegroundColor Green

# Install new service
Write-Host "`n4. Installing new service..." -ForegroundColor Yellow
$exePath = Join-Path $PSScriptRoot "artifacts\bin\Debug\BackupService.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "   ERROR: BackupService.exe not found at: $exePath" -ForegroundColor Red
    exit 1
}

# Get version from file
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrEmpty($version)) {
    $version = $versionInfo.FileVersion
}

sc.exe create BackupRestoreService binPath= "$exePath" start= auto DisplayName= "Backup Restore Service"

if ($LASTEXITCODE -ne 0) {
    Write-Host "   SERVICE CREATION FAILED!" -ForegroundColor Red
    exit 1
}

# Set service description with version
$description = "Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version $version)"
sc.exe description BackupRestoreService "$description"

Write-Host "   Service created!" -ForegroundColor Green
Write-Host "   Description: $description" -ForegroundColor Gray

# Start service
Write-Host "`n5. Starting service..." -ForegroundColor Yellow
sc.exe start BackupRestoreService

Start-Sleep -Seconds 3

# Verify service is running
$status = sc.exe query BackupRestoreService
if ($status -match "RUNNING") {
    Write-Host "   Service is RUNNING!" -ForegroundColor Green
} else {
    Write-Host "   WARNING: Service may not have started" -ForegroundColor Yellow
    Write-Host $status
}

# Verify named pipe exists
Write-Host "`n6. Verifying named pipe..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
$pipes = [System.IO.Directory]::GetFiles("\\.\pipe\") | Select-String "BackupRestore"
if ($pipes) {
    Write-Host "   Named pipe exists: $pipes" -ForegroundColor Green
} else {
    Write-Host "   WARNING: Named pipe not found!" -ForegroundColor Yellow
}

Write-Host "`n=== Installation Complete ===" -ForegroundColor Cyan
Write-Host "Test communication with: .\Test-NamedPipe.ps1" -ForegroundColor White
