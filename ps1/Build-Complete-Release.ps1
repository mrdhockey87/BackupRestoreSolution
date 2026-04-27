# Build-Complete-Release.ps1
# Builds ALL projects including C++ BackupEngine for Release configuration

Write-Host "=== Complete Release Build ===" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Continue"
$solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Find MSBuild
Write-Host "Finding MSBuild..." -ForegroundColor Yellow
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild `
    -find MSBuild\**\Bin\MSBuild.exe `
    -prerelease | Select-Object -First 1

if (-not $msbuild) {
    Write-Host "✗ MSBuild not found!" -ForegroundColor Red
    Write-Host "Visual Studio 2022 with C++ tools must be installed" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Found MSBuild: $msbuild" -ForegroundColor Green
Write-Host ""

# Step 1: Build C++ BackupEngine
Write-Host "Step 1: Building C++ BackupEngine..." -ForegroundColor Cyan
$backupEngineProj = Join-Path $solutionDir "BackupEngine\BackupEngine.vcxproj"

& $msbuild $backupEngineProj /p:Configuration=Release /p:Platform=x64 /m /v:minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ BackupEngine build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ BackupEngine built successfully" -ForegroundColor Green
Write-Host ""

# Step 2: Copy DLL to artifacts
Write-Host "Step 2: Copying SecureServerBackupEngine.dll to artifacts..." -ForegroundColor Cyan
$sourceDll = Join-Path $solutionDir "artifacts\bin\Release\SecureServerBackupEngine.dll"
$destDir = Join-Path $solutionDir "artifacts\bin\Release"
$destDll = Join-Path $destDir "SecureServerBackupEngine.dll"

if (Test-Path $sourceDll) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Copy-Item $sourceDll $destDll -Force
    Write-Host "✓ Copied $sourceDll" -ForegroundColor Green
    Write-Host "  → $destDll" -ForegroundColor Gray
} else {
    Write-Host "✗ SecureServerBackupEngine.dll not found at: $sourceDll" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 3: Build .NET projects
Write-Host "Step 3: Building .NET projects..." -ForegroundColor Cyan

# Build BackupService
Write-Host "  Building BackupService..." -ForegroundColor Yellow
dotnet build "$solutionDir\BackupService\BackupService.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ BackupService build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ BackupService built" -ForegroundColor Green

# Build BackupUI
Write-Host "  Building BackupUI..." -ForegroundColor Yellow
dotnet build "$solutionDir\BackupUI\BackupUI.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ BackupUI build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ BackupUI built" -ForegroundColor Green
Write-Host ""

# Step 4: Verify all files are in place
Write-Host "Step 4: Verifying build output..." -ForegroundColor Cyan
$releaseDir = Join-Path $solutionDir "artifacts\bin\Release"

$requiredFiles = @(
    "BackupUI.exe",
    "BackupUI.dll",
    "BackupUI.runtimeconfig.json",
    "BackupUI.deps.json",
    "BackupService.exe",
    "BackupService.dll",
    "BackupService.runtimeconfig.json",
    "BackupService.deps.json",
    "SecureServerBackupEngine.dll"
)

$allGood = $true
foreach ($file in $requiredFiles) {
    $filePath = Join-Path $releaseDir $file
    if (Test-Path $filePath) {
        $size = (Get-Item $filePath).Length
        Write-Host "  ✓ $file ($([math]::Round($size/1KB, 1)) KB)" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $file MISSING!" -ForegroundColor Red
        $allGood = $false
    }
}

Write-Host ""

if ($allGood) {
    Write-Host "=== BUILD SUCCESSFUL ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now run:" -ForegroundColor Yellow
    Write-Host "  $releaseDir\BackupUI.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "No more 'SecureServerBackupEngine.dll not found' error!" -ForegroundColor Green
} else {
    Write-Host "=== BUILD INCOMPLETE ===" -ForegroundColor Red
    Write-Host "Some files are missing. Check errors above." -ForegroundColor Yellow
}

Write-Host ""

