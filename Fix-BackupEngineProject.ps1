# Fix BackupEngine.vcxproj GUID
# This script updates the project GUID to the standard Visual C++ project GUID

Write-Host "Fixing BackupEngine.vcxproj..." -ForegroundColor Yellow

$vcxprojPath = "BackupEngine\BackupEngine.vcxproj"

if (-not (Test-Path $vcxprojPath)) {
    Write-Host "ERROR: Cannot find BackupEngine.vcxproj" -ForegroundColor Red
    Write-Host "Current directory: $(Get-Location)" -ForegroundColor Red
    exit 1
}

Write-Host "Found project file at: $vcxprojPath" -ForegroundColor Green

# Read the file
$content = Get-Content $vcxprojPath -Raw

# Backup original file
$backupPath = "$vcxprojPath.backup"
Copy-Item $vcxprojPath $backupPath -Force
Write-Host "Created backup at: $backupPath" -ForegroundColor Cyan

# Fix the GUID to standard C++ project GUID
$content = $content -replace '<ProjectGuid>\{[A-F0-9-]+\}</ProjectGuid>', '<ProjectGuid>{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}</ProjectGuid>'

# Add Keyword if not present
if ($content -notmatch '<Keyword>') {
    $content = $content -replace '(<VCProjectVersion>17\.0</VCProjectVersion>)', "`$1`n    <Keyword>Win32Proj</Keyword>"
}

# Save the updated file
Set-Content $vcxprojPath -Value $content -Encoding UTF8 -NoNewline

Write-Host "? Updated BackupEngine.vcxproj with correct GUID" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Close Visual Studio if it's open" -ForegroundColor White
Write-Host "2. Reopen the solution" -ForegroundColor White
Write-Host "3. The BackupEngine project should now load correctly" -ForegroundColor White
Write-Host ""
Write-Host "If you need to restore the original file, rename:" -ForegroundColor Cyan
Write-Host "   $backupPath" -ForegroundColor Cyan
Write-Host "   to: $vcxprojPath" -ForegroundColor Cyan
