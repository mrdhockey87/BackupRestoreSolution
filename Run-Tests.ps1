param(
    [string]$Configuration = "Debug",
    [switch]$RunLinuxRestoreTests
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Running SecureServerBackup test suite" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Invoke-Step {
    param(
        [string]$Message,
        [scriptblock]$Action
    )

    Write-Host $Message -ForegroundColor Yellow
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed with exit code ${LASTEXITCODE}: $Message"
    }
    Write-Host "OK" -ForegroundColor Green
    Write-Host ""
}

Invoke-Step "Running BackupCommon.Tests..." {
    dotnet test .\BackupCommon.Tests\BackupCommon.Tests.csproj -c $Configuration --verbosity minimal
}

Invoke-Step "Running BackupService.Tests..." {
    dotnet test .\BackupService.Tests\BackupService.Tests.csproj -c $Configuration --verbosity minimal
}

Invoke-Step "Running BackupUI.Tests..." {
    dotnet test .\BackupUI.Tests\BackupUI.Tests.csproj -c $Configuration --verbosity minimal
}

$msbuildPath = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (Test-Path $msbuildPath) {
    Invoke-Step "Building BackupEngine.Tests..." {
        & $msbuildPath .\BackupEngine.Tests\BackupEngine.Tests.vcxproj /p:Configuration=$Configuration /p:Platform=x64 /m
    }

    $nativeTestPath = Join-Path $repoRoot "artifacts\bin\$Configuration\BackupEngine.Tests.exe"
    if (Test-Path $nativeTestPath) {
        Invoke-Step "Running BackupEngine.Tests..." {
            & $nativeTestPath
        }
    }
}
else {
    Write-Warning "MSBuild.exe was not found. Skipping BackupEngine.Tests."
}

if ($RunLinuxRestoreTests) {
    $wslPath = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if (-not $wslPath) {
        Write-Warning "WSL was not found. Skipping LinuxRestore tests."
    }
    else {
        $linuxRestorePath = Join-Path $repoRoot "LinuxRestore"
        $resolvedLinuxRestorePath = (Resolve-Path $linuxRestorePath).Path
        $driveLetter = $resolvedLinuxRestorePath.Substring(0, 1).ToLowerInvariant()
        $restOfPath = $resolvedLinuxRestorePath.Substring(2).Replace('\', '/')
        $wslLinuxRestorePath = "/mnt/$driveLetter$restOfPath"

        Invoke-Step "Configuring LinuxRestore tests in WSL..." {
            wsl.exe bash -lc "set -e; cd '$wslLinuxRestorePath'; cmake -S . -B build-test -DSSB_RESTORE_BUILD_TUI=OFF"
        }

        Invoke-Step "Building LinuxRestore restore_engine_tests in WSL..." {
            wsl.exe bash -lc "set -e; cd '$wslLinuxRestorePath'; cmake --build build-test --target restore_engine_tests"
        }

        Invoke-Step "Running LinuxRestore restore_engine_tests in WSL..." {
            wsl.exe bash -lc "set -e; cd '$wslLinuxRestorePath'; ./build-test/restore_engine_tests"
        }
    }
}
else {
    Write-Warning "LinuxRestore test execution is skipped by default. Re-run with -RunLinuxRestoreTests in a WSL environment to execute restore_engine_tests."
}

Write-Host "All available tests completed." -ForegroundColor Cyan
