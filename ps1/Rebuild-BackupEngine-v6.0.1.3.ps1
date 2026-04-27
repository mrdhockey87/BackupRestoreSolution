#!/usr/bin/env pwsh
# Rebuild-BackupEngine-v6.0.1.3.ps1
# Stops service, rebuilds BackupEngine with WIM_FLAG_REFERENCE fix, restarts service

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BackupEngine Rebuild - Version 6.0.1.3" -ForegroundColor Cyan
Write-Host "Fix: Removed WIM_FLAG_REFERENCE from CreateWimFile()" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "❌ This script requires Administrator privileges!" -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    exit 1
}

# Stop BackupRestoreService
Write-Host "🛑 Stopping BackupRestoreService..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name "BackupRestoreService" -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -eq "Running") {
            Stop-Service -Name "BackupRestoreService" -Force -ErrorAction Stop
            Write-Host "✓ Service stopped successfully" -ForegroundColor Green
            Start-Sleep -Seconds 2
        } else {
            Write-Host "ℹ️ Service was not running" -ForegroundColor Gray
        }
    } else {
        Write-Host "ℹ️ Service not installed" -ForegroundColor Gray
    }
} catch {
    Write-Host "⚠️ Could not stop service: $_" -ForegroundColor Yellow
    Write-Host "Continuing anyway..." -ForegroundColor Yellow
}

# Kill any BackupService.exe processes that might be holding the DLL
Write-Host ""
Write-Host "🔍 Checking for BackupService.exe processes..." -ForegroundColor Yellow
$processes = Get-Process -Name "BackupService" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Found $($processes.Count) BackupService.exe process(es), terminating..." -ForegroundColor Yellow
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host "✓ Processes terminated" -ForegroundColor Green
} else {
    Write-Host "ℹ️ No BackupService.exe processes found" -ForegroundColor Gray
}

# Clean old artifacts
Write-Host ""
Write-Host "🧹 Cleaning old build artifacts..." -ForegroundColor Yellow
$artifactsPath = "artifacts\bin\Debug\SecureServerBackupEngine.dll"
if (Test-Path $artifactsPath) {
    Remove-Item $artifactsPath -Force -ErrorAction SilentlyContinue
    Write-Host "✓ Deleted old SecureServerBackupEngine.dll" -ForegroundColor Green
} else {
    Write-Host "ℹ️ No old SecureServerBackupEngine.dll found" -ForegroundColor Gray
}

# Find MSBuild
Write-Host ""
Write-Host "🔍 Locating MSBuild..." -ForegroundColor Yellow
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Host "❌ vswhere.exe not found. Is Visual Studio installed?" -ForegroundColor Red
    exit 1
}

$msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuildPath) {
    Write-Host "❌ MSBuild not found!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Found MSBuild: $msbuildPath" -ForegroundColor Green

# Build BackupEngine
Write-Host ""
Write-Host "🔨 Building BackupEngine (Release/x64)..." -ForegroundColor Yellow
Write-Host "This may take 30-60 seconds..." -ForegroundColor Gray

$buildOutput = & $msbuildPath BackupEngine\BackupEngine.vcxproj `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /v:minimal `
    2>&1

$buildSuccess = $LASTEXITCODE -eq 0

if ($buildSuccess) {
    Write-Host "✓ BackupEngine built successfully!" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    Write-Host ""
    Write-Host "Build output:" -ForegroundColor Yellow
    $buildOutput | ForEach-Object { Write-Host $_ -ForegroundColor Gray }
    exit 1
}

# Verify SecureServerBackupEngine.dll exists
Write-Host ""
Write-Host "🔍 Verifying SecureServerBackupEngine.dll..." -ForegroundColor Yellow
$dllPath = "artifacts\bin\Release\SecureServerBackupEngine.dll"
if (Test-Path $dllPath) {
    $dllInfo = Get-Item $dllPath
    Write-Host "✓ SecureServerBackupEngine.dll found" -ForegroundColor Green
    Write-Host "  Location: $dllPath" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($dllInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "  Modified: $($dllInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "❌ SecureServerBackupEngine.dll not found at $dllPath" -ForegroundColor Red
    exit 1
}

# Copy to artifacts directory
Write-Host ""
Write-Host "📦 Copying SecureServerBackupEngine.dll to artifacts..." -ForegroundColor Yellow
$destPath = "artifacts\bin\Release"
if (-not (Test-Path $destPath)) {
    New-Item -ItemType Directory -Path $destPath -Force | Out-Null
}

Copy-Item $dllPath "$destPath\SecureServerBackupEngine.dll" -Force
if (Test-Path "$destPath\SecureServerBackupEngine.dll") {
    Write-Host "✓ Copied to $destPath\SecureServerBackupEngine.dll" -ForegroundColor Green
} else {
    Write-Host "❌ Failed to copy to artifacts directory" -ForegroundColor Red
    exit 1
}

# Restart service
Write-Host ""
Write-Host "▶️ Restarting BackupRestoreService..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name "BackupRestoreService" -ErrorAction SilentlyContinue
    if ($service) {
        Start-Service -Name "BackupRestoreService" -ErrorAction Stop
        Start-Sleep -Seconds 2
        
        $service = Get-Service -Name "BackupRestoreService"
        if ($service.Status -eq "Running") {
            Write-Host "✓ Service started successfully" -ForegroundColor Green
        } else {
            Write-Host "⚠️ Service status: $($service.Status)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "ℹ️ Service not installed - skipping restart" -ForegroundColor Gray
    }
} catch {
    Write-Host "⚠️ Could not restart service: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✅ REBUILD COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Test full backup creation (should succeed now)" -ForegroundColor White
Write-Host "2. Test incremental backup opening full backup" -ForegroundColor White
Write-Host "3. Update version to 6.0.1.3 if tests pass" -ForegroundColor White
Write-Host ""
Write-Host "What was fixed:" -ForegroundColor Cyan
Write-Host "• CreateWimFile() now uses flags=0 (no flags)" -ForegroundColor White
Write-Host "• WIM_FLAG_REFERENCE removed from WIM creation" -ForegroundColor White
Write-Host "• WIM_FLAG_REFERENCE kept for incremental/differential opening" -ForegroundColor White
Write-Host ""


