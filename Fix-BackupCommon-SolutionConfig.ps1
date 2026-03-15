# Fix-BackupCommon-SolutionConfig.ps1
# Fixes the BackupCommon project configuration mismatch in BackupRestoreSolution.sln
# Run this script with Visual Studio CLOSED to avoid file locking issues

$solutionFile = "BackupRestoreSolution.sln"

Write-Host "Fixing BackupCommon project configurations in $solutionFile..." -ForegroundColor Cyan

# Check if file exists
if (-not (Test-Path $solutionFile)) {
    Write-Host "ERROR: Solution file not found: $solutionFile" -ForegroundColor Red
    Write-Host "Make sure you run this script from the solution directory" -ForegroundColor Yellow
    exit 1
}

# Read the solution file
$content = Get-Content $solutionFile -Raw

# Count how many replacements we'll make
$matchCount = ([regex]::Matches($content, "{FE27AFB3-8F0B-4002-8022-0030748B63F4}.*Any CPU")).Count
Write-Host "Found $matchCount 'Any CPU' references for BackupCommon project" -ForegroundColor Yellow

# Replace all BackupCommon configurations from Any CPU to x64
$newContent = $content -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|x64\.ActiveCfg = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|x64\.Build\.0 = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|Any CPU\.ActiveCfg = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|Any CPU\.Build\.0 = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|x86\.ActiveCfg = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Debug\|x86\.Build\.0 = )Debug\|Any CPU', '$1Debug|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|x64\.ActiveCfg = )Release\|Any CPU', '$1Release|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|x64\.Build\.0 = )Release\|Any CPU', '$1Release|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|Any CPU\.ActiveCfg = )Release\|Any CPU', '$1Release|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|Any CPU\.Build\.0 = )Release\|Any CPU', '$1Release|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|x86\.ActiveCfg = )Release\|Any CPU', '$1Release|x64' -replace `
    '(\{FE27AFB3-8F0B-4002-8022-0030748B63F4\}\.Release\|x86\.Build\.0 = )Release\|Any CPU', '$1Release|x64'

# Count replacements made
$newMatchCount = ([regex]::Matches($newContent, "{FE27AFB3-8F0B-4002-8022-0030748B63F4}.*Any CPU")).Count
$replacementsMade = $matchCount - $newMatchCount

if ($replacementsMade -eq 0) {
    Write-Host "No changes needed - configurations already correct!" -ForegroundColor Green
    exit 0
}

Write-Host "Making $replacementsMade configuration changes..." -ForegroundColor Yellow

# Backup original file
$backupFile = "$solutionFile.backup"
Copy-Item $solutionFile $backupFile -Force
Write-Host "Backup created: $backupFile" -ForegroundColor Green

# Write the updated content
try {
    Set-Content $solutionFile -Value $newContent -NoNewline -Force
    Write-Host "SUCCESS: Solution file updated!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Changes made:" -ForegroundColor Cyan
    Write-Host "  - All BackupCommon Debug configurations now use Debug|x64" -ForegroundColor White
    Write-Host "  - All BackupCommon Release configurations now use Release|x64" -ForegroundColor White
    Write-Host ""
    Write-Host "What to do next:" -ForegroundColor Cyan
    Write-Host "  1. Reload the solution in Visual Studio" -ForegroundColor White
    Write-Host "  2. The configuration warning should no longer appear" -ForegroundColor White
    Write-Host "  3. If issues persist, delete the .vs folder and restart Visual Studio" -ForegroundColor White
} catch {
    Write-Host "ERROR: Failed to write solution file: $_" -ForegroundColor Red
    Write-Host "Restoring backup..." -ForegroundColor Yellow
    Copy-Item $backupFile $solutionFile -Force
    Write-Host "Backup restored. Please close Visual Studio and try again." -ForegroundColor Yellow
    exit 1
}
