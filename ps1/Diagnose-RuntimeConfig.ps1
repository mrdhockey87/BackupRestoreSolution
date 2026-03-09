# Diagnose-RuntimeConfig.ps1
# Comprehensive diagnostic for runtime config issues

Write-Host "=== Runtime Configuration Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

$solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Check all expected runtime config files
$configs = @{
    "Debug BackupUI" = @{
        Exe = "artifacts\bin\Debug\BackupUI.exe"
        RuntimeConfig = "artifacts\bin\Debug\BackupUI.runtimeconfig.json"
        Deps = "artifacts\bin\Debug\BackupUI.deps.json"
    }
    "Debug BackupService" = @{
        Exe = "artifacts\bin\Debug\BackupService.exe"
        RuntimeConfig = "artifacts\bin\Debug\BackupService.runtimeconfig.json"
        Deps = "artifacts\bin\Debug\BackupService.deps.json"
    }
    "Release BackupUI" = @{
        Exe = "artifacts\bin\Release\BackupUI.exe"
        RuntimeConfig = "artifacts\bin\Release\BackupUI.runtimeconfig.json"
        Deps = "artifacts\bin\Release\BackupUI.deps.json"
    }
    "Release BackupService" = @{
        Exe = "artifacts\bin\Release\BackupService.exe"
        RuntimeConfig = "artifacts\bin\Release\BackupService.runtimeconfig.json"
        Deps = "artifacts\bin\Release\BackupService.deps.json"
    }
}

$allGood = $true

foreach ($config in $configs.Keys) {
    Write-Host "Checking: $config" -ForegroundColor Yellow
    
    $exe = Join-Path $solutionDir $configs[$config].Exe
    $runtime = Join-Path $solutionDir $configs[$config].RuntimeConfig
    $deps = Join-Path $solutionDir $configs[$config].Deps
    
    if (Test-Path $exe) {
        Write-Host "  ✓ EXE exists: $exe" -ForegroundColor Green
    } else {
        Write-Host "  ✗ EXE missing: $exe" -ForegroundColor Red
        Write-Host "    → Build this configuration first!" -ForegroundColor Yellow
        continue
    }
    
    if (Test-Path $runtime) {
        Write-Host "  ✓ Runtime config exists: $runtime" -ForegroundColor Green
        
        # Validate JSON
        try {
            $json = Get-Content $runtime -Raw | ConvertFrom-Json
            Write-Host "    → Valid JSON with framework: $($json.runtimeOptions.framework.name)" -ForegroundColor Gray
        } catch {
            Write-Host "    ⚠️  Invalid JSON!" -ForegroundColor Red
            $allGood = $false
        }
    } else {
        Write-Host "  ✗ Runtime config MISSING: $runtime" -ForegroundColor Red
        $allGood = $false
        
        # Search for it in obj folders
        $projectName = if ($config -like "*BackupUI*") { "BackupUI" } else { "BackupService" }
        $configuration = if ($config -like "Debug*") { "Debug" } else { "Release" }
        
        Write-Host "    → Searching in obj folders..." -ForegroundColor Gray
        
        $objPaths = @(
            "artifacts\obj\$projectName\$configuration",
            "$projectName\obj\$configuration",
            "artifacts\obj\$projectName"
        )
        
        $found = $false
        foreach ($objPath in $objPaths) {
            $objFile = Join-Path $solutionDir (Join-Path $objPath "$projectName.runtimeconfig.json")
            if (Test-Path $objFile) {
                Write-Host "    → Found in obj: $objFile" -ForegroundColor Cyan
                Write-Host "    → Copying to bin..." -ForegroundColor Yellow
                Copy-Item $objFile $runtime -Force
                Write-Host "    ✓ Copied!" -ForegroundColor Green
                $found = $true
                break
            }
        }
        
        if (-not $found) {
            Write-Host "    ✗ Not found in any obj folder!" -ForegroundColor Red
            Write-Host "    → This means MSBuild is not generating it at all!" -ForegroundColor Red
        }
    }
    
    if (Test-Path $deps) {
        Write-Host "  ✓ Deps file exists: $deps" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Deps file MISSING: $deps" -ForegroundColor Red
        $allGood = $false
    }
    
    Write-Host ""
}

Write-Host "=== Checking MSBuild Properties ===" -ForegroundColor Cyan
Write-Host ""

# Check Directory.Build.props
$buildProps = Join-Path $solutionDir "Directory.Build.props"
if (Test-Path $buildProps) {
    Write-Host "✓ Directory.Build.props exists" -ForegroundColor Green
    
    $content = Get-Content $buildProps -Raw
    
    if ($content -match "GenerateRuntimeConfigurationFiles.*true") {
        Write-Host "  ✓ GenerateRuntimeConfigurationFiles is set to true" -ForegroundColor Green
    } else {
        Write-Host "  ✗ GenerateRuntimeConfigurationFiles NOT FOUND or not true!" -ForegroundColor Red
        Write-Host "    → This is the root cause!" -ForegroundColor Red
        $allGood = $false
    }
} else {
    Write-Host "✗ Directory.Build.props NOT FOUND!" -ForegroundColor Red
    $allGood = $false
}

Write-Host ""
Write-Host "=== Recommended Actions ===" -ForegroundColor Cyan
Write-Host ""

if (-not $allGood) {
    Write-Host "Issues found! Try these steps:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Close Visual Studio" -ForegroundColor White
    Write-Host "2. Run: dotnet clean" -ForegroundColor White
    Write-Host "3. Delete bin and obj folders:" -ForegroundColor White
    Write-Host "   Remove-Item artifacts -Recurse -Force" -ForegroundColor Gray
    Write-Host "   Remove-Item BackupUI\bin,BackupUI\obj -Recurse -Force" -ForegroundColor Gray
    Write-Host "   Remove-Item BackupService\bin,BackupService\obj -Recurse -Force" -ForegroundColor Gray
    Write-Host "4. Run: dotnet restore" -ForegroundColor White
    Write-Host "5. Run: dotnet build -c Debug" -ForegroundColor White
    Write-Host "6. Check if runtime config files appear" -ForegroundColor White
    Write-Host "7. If still failing, run this script again to see new diagnostics" -ForegroundColor White
} else {
    Write-Host "✓ All runtime config files present and valid!" -ForegroundColor Green
    Write-Host "Applications should launch without .NET install error" -ForegroundColor Green
}

Write-Host ""
