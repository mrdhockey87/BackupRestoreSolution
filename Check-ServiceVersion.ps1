# Check which version of BackupService is actually installed/running

Write-Host "=== BackupRestoreService Version Check ===" -ForegroundColor Cyan

# Get service path
$servicePath = (Get-WmiObject win32_service | Where-Object {$_.Name -eq 'BackupRestoreService'}).PathName
if ($servicePath) {
    Write-Host "`nInstalled Service Path:" -ForegroundColor Yellow
    Write-Host "  $servicePath" -ForegroundColor White
    
    # Check if file exists
    $exePath = $servicePath -replace '"', ''
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Host "`nFile Info:" -ForegroundColor Yellow
        Write-Host "  Last Modified: $($fileInfo.LastWriteTime)" -ForegroundColor White
        Write-Host "  Size: $($fileInfo.Length) bytes" -ForegroundColor White
        
        # Check file version
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
        Write-Host "`nFile Version:" -ForegroundColor Yellow
        Write-Host "  Product Version: $($versionInfo.ProductVersion)" -ForegroundColor White
        Write-Host "  File Version: $($versionInfo.FileVersion)" -ForegroundColor White
    } else {
        Write-Host "  ERROR: File not found!" -ForegroundColor Red
    }
} else {
    Write-Host "`nERROR: BackupRestoreService not found!" -ForegroundColor Red
}

# Check latest build
$latestBuild = "artifacts\bin\Debug\BackupService.exe"
if (Test-Path $latestBuild) {
    $buildInfo = Get-Item $latestBuild
    Write-Host "`nLatest Build:" -ForegroundColor Yellow
    Write-Host "  Path: $latestBuild" -ForegroundColor White
    Write-Host "  Last Modified: $($buildInfo.LastWriteTime)" -ForegroundColor White
    
    $buildVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($latestBuild)
    Write-Host "  Product Version: $($buildVersionInfo.ProductVersion)" -ForegroundColor White
    Write-Host "  File Version: $($buildVersionInfo.FileVersion)" -ForegroundColor White
    
    # Compare
    if ($servicePath -and (Test-Path $exePath)) {
        $installedTime = (Get-Item $exePath).LastWriteTime
        $builtTime = $buildInfo.LastWriteTime
        
        Write-Host "`nComparison:" -ForegroundColor Yellow
        if ($builtTime -gt $installedTime) {
            Write-Host "  ??  NEWER BUILD AVAILABLE - Need to reinstall service!" -ForegroundColor Red
            Write-Host "     Run: .\Reinstall-Service.ps1" -ForegroundColor White
        } else {
            Write-Host "  ? Service is running latest build" -ForegroundColor Green
        }
    }
} else {
    Write-Host "`nWARNING: Latest build not found at $latestBuild" -ForegroundColor Yellow
    Write-Host "Run: dotnet build BackupService\BackupService.csproj" -ForegroundColor White
}

Write-Host ""
