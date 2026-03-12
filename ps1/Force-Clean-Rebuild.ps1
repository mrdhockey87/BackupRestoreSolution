# Force-Clean-Rebuild.ps1
# Completely cleans MSBuild cache and rebuilds to force runtime config generation

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "FORCE CLEAN AND REBUILD" -ForegroundColor Cyan
Write-Host "Fixing BackupService.runtimeconfig.json issue" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

# Stop and remove service if running
Write-Host "`n[1/5] Stopping BackupRestoreService..." -ForegroundColor Yellow
net stop BackupRestoreService 2>$null | Out-Null
Start-Sleep -Seconds 1
sc delete BackupRestoreService 2>$null | Out-Null
Write-Host "      Service stopped (if it was running)" -ForegroundColor Green

# Delete ALL build artifacts
Write-Host "`n[2/5] Deleting all build artifacts..." -ForegroundColor Yellow
$foldersToDelete = @(
    "artifacts",
    "BackupUI\bin",
    "BackupUI\obj",
    "BackupService\bin",
    "BackupService\obj",
    "BackupEngine\x64",
    ".vs"
)

foreach ($folder in $foldersToDelete) {
    if (Test-Path $folder) {
        Write-Host "      Deleting: $folder" -ForegroundColor Gray
        Remove-Item -Path $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Host "      All artifacts deleted" -ForegroundColor Green

# Find MSBuild
Write-Host "`n[3/5] Locating MSBuild..." -ForegroundColor Yellow
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    Write-Host "      Found: $msbuildPath" -ForegroundColor Green
} else {
    Write-Host "      ERROR: vswhere.exe not found!" -ForegroundColor Red
    exit 1
}

# Restore NuGet packages
Write-Host "`n[4/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore BackupRestoreSolution.sln
if ($LASTEXITCODE -eq 0) {
    Write-Host "      Packages restored successfully" -ForegroundColor Green
} else {
    Write-Host "      ERROR: Package restore failed!" -ForegroundColor Red
    exit 1
}

# Build with detailed verbosity to see runtime config generation
Write-Host "`n[5/5] Building solution (this will take a moment)..." -ForegroundColor Yellow
Write-Host "      Configuration: Debug" -ForegroundColor Gray
Write-Host "      Platform: x64" -ForegroundColor Gray

& $msbuildPath BackupRestoreSolution.sln `
    /t:Rebuild `
    /p:Configuration=Debug `
    /p:Platform=x64 `
    /v:detailed `
    /fl `
    /flp:logfile=build.log

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n      BUILD SUCCESSFUL!" -ForegroundColor Green
} else {
    Write-Host "`n      BUILD FAILED! Check build.log for details" -ForegroundColor Red
    exit 1
}

# Verify runtime config files
Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "VERIFICATION - Checking Runtime Config Files" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

$runtimeConfigs = @(
    "artifacts\bin\Debug\BackupUI.runtimeconfig.json",
    "artifacts\bin\Debug\BackupService.runtimeconfig.json"
)

$allExist = $true
foreach ($config in $runtimeConfigs) {
    $exists = Test-Path $config
    $status = if ($exists) { "OK EXISTS" } else { "ERROR MISSING"; $allExist = $false }
    $color = if ($exists) { "Green" } else { "Red" }

    Write-Host "$status : $config" -ForegroundColor $color

    if ($exists) {
        $size = (Get-Item $config).Length
        Write-Host "           Size: $size bytes" -ForegroundColor Gray
    }
}

if ($allExist) {
    Write-Host "`n SUCCESS! All runtime config files generated correctly!" -ForegroundColor Green
    Write-Host "          You can now run BackupUI.exe and BackupService.exe" -ForegroundColor Green
} else {
    Write-Host "`n WARNING: Some runtime config files are missing!" -ForegroundColor Yellow
    Write-Host "          Check build.log for GenerateBuildRuntimeConfigurationFiles target" -ForegroundColor Yellow
    Write-Host "          Search for: GenerateBuildRuntimeConfigurationFiles" -ForegroundColor Gray
}

Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "Build log saved to: build.log" -ForegroundColor Gray
Write-Host "================================================" -ForegroundColor Cyan
