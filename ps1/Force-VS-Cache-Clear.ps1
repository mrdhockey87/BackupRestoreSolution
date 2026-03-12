# Force-VS-Cache-Clear.ps1
# Completely clears ALL Visual Studio and MSBuild caches
# Ensures Directory.Build.props changes are recognized
# Run this when properties change but VS doesn't rebuild correctly

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "FORCE VISUAL STUDIO CACHE CLEAR" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$solutionRoot = $PSScriptRoot

# Step 1: Check if Visual Studio is running
Write-Host "[1/6] Checking for running Visual Studio instances..." -ForegroundColor Yellow
$vsProcesses = Get-Process devenv -ErrorAction SilentlyContinue
if ($vsProcesses) {
    Write-Host "  ⚠ WARNING: Visual Studio is currently running!" -ForegroundColor Red
    Write-Host "  ⚠ You MUST close Visual Studio for this script to work!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Close Visual Studio now and press any key to continue..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    
    # Check again
    $vsProcesses = Get-Process devenv -ErrorAction SilentlyContinue
    if ($vsProcesses) {
        Write-Host "  ✗ Visual Studio is STILL running! Cannot continue." -ForegroundColor Red
        Write-Host "  Please close Visual Studio completely and run this script again." -ForegroundColor Red
        pause
        exit 1
    }
}
Write-Host "  ✓ Visual Studio is not running" -ForegroundColor Green

# Step 2: Delete .vs folder (VS cache)
Write-Host ""
Write-Host "[2/6] Deleting .vs folder (Visual Studio cache)..." -ForegroundColor Yellow
$vsFolder = Join-Path $solutionRoot ".vs"
if (Test-Path $vsFolder) {
    try {
        Remove-Item -Path $vsFolder -Recurse -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted .vs folder" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Could not delete .vs folder: $_" -ForegroundColor Red
        Write-Host "  Try closing all Explorer windows and running again" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ℹ .vs folder doesn't exist (already clean)" -ForegroundColor Gray
}

# Step 3: Delete artifacts folder
Write-Host ""
Write-Host "[3/6] Deleting artifacts folder (all build output)..." -ForegroundColor Yellow
$artifactsFolder = Join-Path $solutionRoot "artifacts"
if (Test-Path $artifactsFolder) {
    try {
        Remove-Item -Path $artifactsFolder -Recurse -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted artifacts folder" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Could not delete artifacts folder: $_" -ForegroundColor Red
    }
} else {
    Write-Host "  ℹ artifacts folder doesn't exist" -ForegroundColor Gray
}

# Step 4: Delete project bin/obj folders (legacy locations)
Write-Host ""
Write-Host "[4/6] Deleting project bin/obj folders..." -ForegroundColor Yellow
$projectFolders = @("BackupUI", "BackupService", "BackupEngine")
$foldersDeleted = 0
foreach ($project in $projectFolders) {
    $projectPath = Join-Path $solutionRoot $project
    if (Test-Path $projectPath) {
        # Delete bin
        $binPath = Join-Path $projectPath "bin"
        if (Test-Path $binPath) {
            Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
            $foldersDeleted++
        }
        # Delete obj
        $objPath = Join-Path $projectPath "obj"
        if (Test-Path $objPath) {
            Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
            $foldersDeleted++
        }
    }
}
if ($foldersDeleted -gt 0) {
    Write-Host "  ✓ Deleted $foldersDeleted bin/obj folders" -ForegroundColor Green
} else {
    Write-Host "  ℹ No legacy bin/obj folders found" -ForegroundColor Gray
}

# Step 5: Delete BackupEngine x64 folder
Write-Host ""
Write-Host "[5/6] Deleting BackupEngine x64 folder..." -ForegroundColor Yellow
$x64Path = Join-Path $solutionRoot "BackupEngine\x64"
if (Test-Path $x64Path) {
    try {
        Remove-Item -Path $x64Path -Recurse -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted BackupEngine\x64 folder" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Could not delete x64 folder: $_" -ForegroundColor Red
    }
} else {
    Write-Host "  ℹ BackupEngine\x64 doesn't exist" -ForegroundColor Gray
}

# Step 6: Clear NuGet cache (optional but thorough)
Write-Host ""
Write-Host "[6/6] Clearing NuGet package cache (optional)..." -ForegroundColor Yellow
Write-Host "  This may take a minute..." -ForegroundColor Gray
try {
    $nugetOutput = dotnet nuget locals all --clear 2>&1
    Write-Host "  ✓ NuGet cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  ⚠ Could not clear NuGet cache (not critical)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "✓ ALL CACHES CLEARED!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Open Visual Studio" -ForegroundColor White
Write-Host ""
Write-Host "  2. In Visual Studio, run:" -ForegroundColor White
Write-Host "     Build → Rebuild Solution" -ForegroundColor Cyan
Write-Host ""
Write-Host "  3. Check for runtime config files:" -ForegroundColor White
Write-Host "     artifacts\bin\Debug\BackupUI.runtimeconfig.json" -ForegroundColor Gray
Write-Host "     artifacts\bin\Debug\BackupService.runtimeconfig.json" -ForegroundColor Gray
Write-Host ""
Write-Host "  4. If STILL missing, run from PowerShell:" -ForegroundColor White
Write-Host "     dotnet build --no-incremental" -ForegroundColor Cyan
Write-Host ""
Write-Host "This forces MSBuild to re-evaluate Directory.Build.props!" -ForegroundColor Green
Write-Host ""
pause
