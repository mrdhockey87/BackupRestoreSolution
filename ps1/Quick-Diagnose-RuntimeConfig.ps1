# Quick-Diagnose-RuntimeConfig.ps1
# Quick diagnostic to check runtime config generation status

Write-Host "`n=== RUNTIME CONFIG DIAGNOSTIC ===" -ForegroundColor Cyan

# Check if artifacts exist
$debugBin = "artifacts\bin\Debug"
$releaseBin = "artifacts\bin\Release"

Write-Host "`n1. BUILD ARTIFACTS:" -ForegroundColor Yellow

if (Test-Path $debugBin) {
    Write-Host "   OK Debug artifacts folder exists" -ForegroundColor Green
    $files = Get-ChildItem $debugBin -Filter "*.runtimeconfig.json"
    if ($files.Count -gt 0) {
        Write-Host "   Found $($files.Count) runtime config file(s):" -ForegroundColor Gray
        foreach ($file in $files) {
            $fileSize = $file.Length
            Write-Host "      - $($file.Name) ($fileSize bytes)" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ERROR No runtime config files found!" -ForegroundColor Red
    }
} else {
    Write-Host "   ERROR Debug artifacts folder does not exist" -ForegroundColor Red
}

# Check intermediate outputs
Write-Host "`n2. INTERMEDIATE BUILD FILES:" -ForegroundColor Yellow

$projects = @("BackupUI", "BackupService")
foreach ($proj in $projects) {
    $objPath = "artifacts\obj\$proj\Debug"
    if (Test-Path $objPath) {
        Write-Host "   OK $proj obj folder exists" -ForegroundColor Green
        $runtimeConfig = "$objPath\$proj.runtimeconfig.json"
        if (Test-Path $runtimeConfig) {
            $size = (Get-Item $runtimeConfig).Length
            Write-Host "      OK Runtime config exists ($size bytes)" -ForegroundColor Green
        } else {
            Write-Host "      ERROR Runtime config missing!" -ForegroundColor Red
            Write-Host "         Expected: $runtimeConfig" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ERROR $proj obj folder does not exist" -ForegroundColor Red
    }
}

# Check project files
Write-Host "`n3. PROJECT CONFIGURATION:" -ForegroundColor Yellow

foreach ($proj in $projects) {
    $csproj = "$proj\$proj.csproj"
    if (Test-Path $csproj) {
        $content = Get-Content $csproj -Raw
        $outputType = if ($content -match '<OutputType>(\w+)</OutputType>') { $matches[1] } else { "NOT SET" }
        $targetFramework = if ($content -match '<TargetFramework>([^<]+)</TargetFramework>') { $matches[1] } else { "NOT SET" }
        
        Write-Host "   $proj.csproj:" -ForegroundColor Gray
        Write-Host "      OutputType: $outputType" -ForegroundColor Gray
        Write-Host "      TargetFramework: $targetFramework" -ForegroundColor Gray
    }
}

# Check Directory.Build.props
Write-Host "`n4. CENTRALIZED BUILD CONFIGURATION:" -ForegroundColor Yellow

if (Test-Path "Directory.Build.props") {
    $content = Get-Content "Directory.Build.props" -Raw
    $settings = @{
        "GenerateRuntimeConfigurationFiles" = $content -match '<GenerateRuntimeConfigurationFiles>(\w+)</GenerateRuntimeConfigurationFiles>'
        "ProduceReferenceAssembly" = $content -match '<ProduceReferenceAssembly>(\w+)</ProduceReferenceAssembly>'
        "RuntimeConfigurationFilesOutputPath" = $content -match '<RuntimeConfigurationFilesOutputPath>'
    }
    
    foreach ($setting in $settings.GetEnumerator()) {
        $status = if ($setting.Value) { "OK" } else { "MISSING" }
        $color = if ($setting.Value) { "Green" } else { "Red" }
        Write-Host "   $status $($setting.Key)" -ForegroundColor $color
    }
} else {
    Write-Host "   ERROR Directory.Build.props not found!" -ForegroundColor Red
}

Write-Host "`n=== RECOMMENDATION ===" -ForegroundColor Cyan

$missingAny = $false
foreach ($proj in $projects) {
    $runtimeConfig = "artifacts\bin\Debug\$proj.runtimeconfig.json"
    if (-not (Test-Path $runtimeConfig)) {
        $missingAny = $true
        Write-Host "Missing: $runtimeConfig" -ForegroundColor Red
    }
}

if ($missingAny) {
    Write-Host "`nAction required:" -ForegroundColor Yellow
    Write-Host "1. Run: .\Force-Clean-Rebuild.ps1" -ForegroundColor White
    Write-Host "   This will completely clean and rebuild to force runtime config generation" -ForegroundColor Gray
    Write-Host "`nOR" -ForegroundColor Yellow
    Write-Host "2. Manually delete artifacts, bin, and obj folders, then rebuild" -ForegroundColor White
} else {
    Write-Host "`n All runtime config files present! OK" -ForegroundColor Green
}

Write-Host ""
