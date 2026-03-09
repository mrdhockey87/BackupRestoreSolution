# Force-GenerateRuntimeConfig.ps1
# Nuclear option: manually generate runtime config files if MSBuild won't

Write-Host "=== Force Runtime Config Generation ===" -ForegroundColor Cyan
Write-Host ""

$solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Create-RuntimeConfig {
    param(
        [string]$ProjectName,
        [string]$Configuration,
        [string]$TargetFramework = "net8.0",
        [string]$RuntimeFramework = "Microsoft.NETCore.App",
        [bool]$IsWinExe = $false
    )
    
    $binPath = Join-Path $solutionDir "artifacts\bin\$Configuration"
    $runtimeConfigPath = Join-Path $binPath "$ProjectName.runtimeconfig.json"
    
    Write-Host "Creating: $runtimeConfigPath" -ForegroundColor Yellow
    
    # Base runtime config
    $config = @{
        runtimeOptions = @{
            tfm = $TargetFramework
            framework = @{
                name = $RuntimeFramework
                version = "8.0.0"
            }
            configProperties = @{
                "System.Reflection.Metadata.MetadataUpdater.IsSupported" = $false
            }
        }
    }
    
    # For WinExe (WPF apps), add WindowsDesktop framework
    if ($IsWinExe) {
        $config.runtimeOptions.frameworks = @(
            @{
                name = "Microsoft.NETCore.App"
                version = "8.0.0"
            },
            @{
                name = "Microsoft.WindowsDesktop.App"
                version = "8.0.0"
            }
        )
        # Remove the single framework entry
        $config.runtimeOptions.Remove("framework")
    }
    
    # Ensure directory exists
    if (-not (Test-Path $binPath)) {
        New-Item -ItemType Directory -Path $binPath -Force | Out-Null
    }
    
    # Write JSON
    $json = $config | ConvertTo-Json -Depth 10
    Set-Content -Path $runtimeConfigPath -Value $json -Encoding UTF8
    
    Write-Host "  ✓ Created: $runtimeConfigPath" -ForegroundColor Green
    
    # Verify
    if (Test-Path $runtimeConfigPath) {
        $size = (Get-Item $runtimeConfigPath).Length
        Write-Host "  ✓ File size: $size bytes" -ForegroundColor Gray
        return $true
    } else {
        Write-Host "  ✗ File creation failed!" -ForegroundColor Red
        return $false
    }
}

# Generate for all configurations
Write-Host "Generating runtime configs for all executables..." -ForegroundColor Cyan
Write-Host ""

$success = $true

# BackupUI - WPF application (WinExe)
Write-Host "BackupUI (WPF - WinExe)" -ForegroundColor Yellow
$success = (Create-RuntimeConfig -ProjectName "BackupUI" -Configuration "Debug" -IsWinExe $true) -and $success
$success = (Create-RuntimeConfig -ProjectName "BackupUI" -Configuration "Release" -IsWinExe $true) -and $success
Write-Host ""

# BackupService - Console application (Exe)
Write-Host "BackupService (Console - Exe)" -ForegroundColor Yellow
$success = (Create-RuntimeConfig -ProjectName "BackupService" -Configuration "Debug" -IsWinExe $false) -and $success
$success = (Create-RuntimeConfig -ProjectName "BackupService" -Configuration "Release" -IsWinExe $false) -and $success
Write-Host ""

if ($success) {
    Write-Host "=== SUCCESS ===" -ForegroundColor Green
    Write-Host "All runtime config files generated manually!" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now run:" -ForegroundColor Yellow
    Write-Host "  artifacts\bin\Debug\BackupUI.exe" -ForegroundColor White
    Write-Host "  artifacts\bin\Release\BackupUI.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "WITHOUT the 'install .NET' error!" -ForegroundColor Green
} else {
    Write-Host "=== FAILED ===" -ForegroundColor Red
    Write-Host "Some runtime config files could not be created!" -ForegroundColor Red
}

Write-Host ""
