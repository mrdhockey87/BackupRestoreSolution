# Check-CppRuntime.ps1
# Diagnoses missing C++ Runtime dependencies for SecureServerBackupEngine.dll

Write-Host "=== C++ Runtime Dependency Checker ===" -ForegroundColor Cyan
Write-Host ""

$solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Check if Visual C++ Redistributable is installed
Write-Host "Checking Visual C++ Redistributables..." -ForegroundColor Yellow
Write-Host ""

$vcRedists = @(
    @{Name="Visual C++ 2015-2022 x64"; Key="HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"},
    @{Name="Visual C++ 2015-2022 x86"; Key="HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"}
)

$missingRedists = @()

foreach ($redist in $vcRedists) {
    if (Test-Path $redist.Key) {
        $version = (Get-ItemProperty $redist.Key).Version
        $installed = (Get-ItemProperty $redist.Key).Installed
        if ($installed -eq 1) {
            Write-Host "✓ $($redist.Name) - Version $version" -ForegroundColor Green
        } else {
            Write-Host "✗ $($redist.Name) - NOT INSTALLED" -ForegroundColor Red
            $missingRedists += $redist.Name
        }
    } else {
        Write-Host "✗ $($redist.Name) - NOT FOUND" -ForegroundColor Red
        $missingRedists += $redist.Name
    }
}

Write-Host ""

# Check BackupEngine.dll location
Write-Host "Checking SecureServerBackupEngine.dll locations..." -ForegroundColor Yellow
Write-Host ""

$dllPaths = @(
    "artifacts\bin\Release\SecureServerBackupEngine.dll",
    "artifacts\bin\Debug\SecureServerBackupEngine.dll",
    "artifacts\bin\Release\SecureServerBackupEngine.dll",
    "artifacts\bin\Debug\SecureServerBackupEngine.dll"
)

foreach ($path in $dllPaths) {
    $fullPath = Join-Path $solutionDir $path
    if (Test-Path $fullPath) {
        Write-Host "✓ Found: $path" -ForegroundColor Green
        
        # Try to get DLL dependencies
        try {
            $dumpbin = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe"
            $dumpbinPath = Get-Item $dumpbin -ErrorAction SilentlyContinue | Select-Object -First 1
            
            if ($dumpbinPath) {
                Write-Host "  Dependencies:" -ForegroundColor Gray
                & $dumpbinPath /DEPENDENTS $fullPath 2>&1 | Select-String "\.dll" | ForEach-Object {
                    $line = $_.Line.Trim()
                    if ($line -match "VCRUNTIME" -or $line -match "MSVCP" -or $line -match "ucrtbase") {
                        Write-Host "    → $line" -ForegroundColor Cyan
                    }
                }
            }
        } catch {
            # Silent fail - dumpbin not available
        }
    } else {
        Write-Host "✗ Missing: $path" -ForegroundColor Red
    }
}

Write-Host ""

# Provide solution
if ($missingRedists.Count -gt 0) {
    Write-Host "=== PROBLEM FOUND ===" -ForegroundColor Red
    Write-Host ""
    Write-Host "SecureServerBackupEngine.dll requires Visual C++ Redistributables that are not installed!" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Missing: $($missingRedists -join ', ')" -ForegroundColor Red
    Write-Host ""
    Write-Host "=== SOLUTION ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Download and install Visual C++ Redistributable:" -ForegroundColor Yellow
    Write-Host "https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "Or run this command to download it:" -ForegroundColor Yellow
    Write-Host "Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile 'vc_redist.x64.exe'" -ForegroundColor White
    Write-Host ""
    Write-Host "Then install: .\vc_redist.x64.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "After installation, the 'SecureServerBackupEngine.dll not found' error will disappear!" -ForegroundColor Green
} else {
    Write-Host "=== ALL CHECKS PASSED ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Visual C++ Redistributables are installed correctly." -ForegroundColor Green
    Write-Host ""
    Write-Host "If you're still seeing the error, try:" -ForegroundColor Yellow
    Write-Host "1. Restart your computer" -ForegroundColor White
    Write-Host "2. Check Windows Event Viewer for detailed error messages" -ForegroundColor White
    Write-Host "3. Run: where.exe SecureServerBackupEngine.dll" -ForegroundColor White
}

Write-Host ""

