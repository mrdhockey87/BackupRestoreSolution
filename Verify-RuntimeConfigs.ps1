# Verify runtime config files exist after build
Write-Host "=== Runtime Config Verification ===" -ForegroundColor Cyan

$outputDir = "artifacts\bin\Debug"

Write-Host "`nChecking output directory: $outputDir" -ForegroundColor Yellow

if (Test-Path $outputDir) {
    $runtimeConfigs = Get-ChildItem $outputDir -Filter "*.runtimeconfig.json"
    $depsFiles = Get-ChildItem $outputDir -Filter "*.deps.json"
    
    Write-Host "`nRuntime Config Files:" -ForegroundColor Green
    if ($runtimeConfigs) {
        $runtimeConfigs | ForEach-Object {
            Write-Host "  ? $($_.Name)" -ForegroundColor Green
            Write-Host "    Last Modified: $($_.LastWriteTime)" -ForegroundColor Gray
            Write-Host "    Size: $($_.Length) bytes" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ? NO RUNTIME CONFIG FILES FOUND!" -ForegroundColor Red
    }
    
    Write-Host "`nDependency Files:" -ForegroundColor Green
    if ($depsFiles) {
        $depsFiles | ForEach-Object {
            Write-Host "  ? $($_.Name)" -ForegroundColor Green
        }
    } else {
        Write-Host "  ?? No deps.json files found" -ForegroundColor Yellow
    }
    
    # Check executable files
    Write-Host "`nExecutables:" -ForegroundColor Green
    $exes = Get-ChildItem $outputDir -Filter "*.exe"
    $exes | ForEach-Object {
        Write-Host "  • $($_.Name)" -ForegroundColor White
        
        # Check if corresponding runtime config exists
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        $runtimeConfig = "$outputDir\$baseName.runtimeconfig.json"
        
        if (Test-Path $runtimeConfig) {
            Write-Host "    ? Has runtime config" -ForegroundColor Green
        } else {
            Write-Host "    ? MISSING runtime config!" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ? Output directory not found!" -ForegroundColor Red
}

Write-Host ""
