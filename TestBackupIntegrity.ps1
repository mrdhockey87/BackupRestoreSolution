# PowerShell script to test backup integrity
param(
    [Parameter(Mandatory=$true)]
    [string]$BackupPath
)

Write-Host "Testing backup integrity for: $BackupPath" -ForegroundColor Yellow
Write-Host ""

# Check if file exists
if (-not (Test-Path $BackupPath)) {
    Write-Host "ERROR: Backup file not found!" -ForegroundColor Red
    exit 1
}

# Get file info
$file = Get-Item $BackupPath
Write-Host "File Information:" -ForegroundColor Green
Write-Host "  Path: $($file.FullName)"
Write-Host "  Size: $([math]::Round($file.Length / 1GB, 2)) GB"
Write-Host "  Created: $($file.CreationTime)"
Write-Host "  Modified: $($file.LastWriteTime)"
Write-Host ""

# Try to call the backup verification from the BackupUI
Write-Host "Attempting to verify backup using BackupUI..." -ForegroundColor Yellow

# Build path to BackupUI executable
$backupUIPath = Join-Path $PSScriptRoot "artifacts\bin\Debug\BackupUI.exe"

if (Test-Path $backupUIPath) {
    Write-Host "Found BackupUI at: $backupUIPath" -ForegroundColor Green
    # Note: You'll need to implement a command-line verification option
    # For now, we'll just suggest manual verification
} else {
    Write-Host "BackupUI not found at expected location" -ForegroundColor Red
    Write-Host "Please verify the backup manually using the BackupUI application" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Manual Verification Steps:" -ForegroundColor Cyan
Write-Host "1. Open BackupUI application"
Write-Host "2. Go to 'Verify' or 'Tools' menu"
Write-Host "3. Select 'Verify Backup' and choose: $BackupPath"
Write-Host "4. If verification passes, the backup is intact"
Write-Host "5. If verification fails, you'll need a new full backup"