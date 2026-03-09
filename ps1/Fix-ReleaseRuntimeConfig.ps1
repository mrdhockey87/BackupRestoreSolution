# Fix-ReleaseRuntimeConfig.ps1
# Fixes missing .runtimeconfig.json files in Release builds

Write-Host "=== Runtime Config Fix for Release Build ===" -ForegroundColor Cyan
Write-Host ""

$solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseBinDir = Join-Path $solutionDir "artifacts\bin\Release"

# Check if Release directory exists
if (-not (Test-Path $releaseBinDir)) {
    Write-Host "ERROR: Release bin directory not found: $releaseBinDir" -ForegroundColor Red
    Write-Host "Please build in Release configuration first!" -ForegroundColor Yellow
    exit 1
}

Write-Host "Checking for executables in: $releaseBinDir" -ForegroundColor Green
Write-Host ""

# Find all .exe files in Release\bin
$exeFiles = Get-ChildItem -Path $releaseBinDir -Filter "*.exe" -File

if ($exeFiles.Count -eq 0) {
    Write-Host "No .exe files found in Release directory!" -ForegroundColor Yellow
    exit 0
}

foreach ($exe in $exeFiles) {
    $exeName = $exe.BaseName
    $runtimeConfigFile = Join-Path $releaseBinDir "$exeName.runtimeconfig.json"
    $depsJsonFile = Join-Path $releaseBinDir "$exeName.deps.json"
    
    Write-Host "Checking: $exeName.exe" -ForegroundColor Cyan
    
    # Check if runtime config exists
    if (Test-Path $runtimeConfigFile) {
        Write-Host "  ✓ Runtime config exists: $exeName.runtimeconfig.json" -ForegroundColor Green
    }
    else {
        Write-Host "  ✗ MISSING: $exeName.runtimeconfig.json" -ForegroundColor Red
        
        # Search for it in obj folders
        $objDirs = @(
            Join-Path $solutionDir "artifacts\obj\$exeName\Release"
            Join-Path $solutionDir "$exeName\obj\Release"
            Join-Path $solutionDir "artifacts\obj\$exeName"
        )
        
        $found = $false
        foreach ($objDir in $objDirs) {
            $sourceFile = Join-Path $objDir "$exeName.runtimeconfig.json"
            if (Test-Path $sourceFile) {
                Write-Host "  → Found in obj: $objDir" -ForegroundColor Yellow
                Copy-Item $sourceFile $runtimeConfigFile -Force
                Write-Host "  ✓ Copied to Release bin" -ForegroundColor Green
                $found = $true
                break
            }
        }
        
        if (-not $found) {
            Write-Host "  ⚠️  Could not find runtime config in obj folders" -ForegroundColor Yellow
            Write-Host "  ℹ️  Solution: Run 'dotnet build -c Release' to regenerate" -ForegroundColor Cyan
        }
    }
    
    # Check if deps.json exists
    if (Test-Path $depsJsonFile) {
        Write-Host "  ✓ Deps file exists: $exeName.deps.json" -ForegroundColor Green
    }
    else {
        Write-Host "  ✗ MISSING: $exeName.deps.json" -ForegroundColor Red
    }
    
    Write-Host ""
}

Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "If files are still missing, run:" -ForegroundColor Yellow
Write-Host "  dotnet build -c Release" -ForegroundColor White
Write-Host "or rebuild the solution in Visual Studio with Release configuration" -ForegroundColor White
Write-Host ""
