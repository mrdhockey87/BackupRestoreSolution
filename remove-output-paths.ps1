# Simple script to remove OutDir and IntDir from BackupEngine.vcxproj
$file = "BackupEngine\BackupEngine.vcxproj"
$lines = Get-Content $file
$output = @()

$skip = $false
foreach ($line in $lines) {
    if ($line -match '<PropertyGroup>') {
        $nextIndex = $lines.IndexOf($line) + 1
        if ($nextIndex -lt $lines.Count -and $lines[$nextIndex] -match '<OutDir>') {
            $skip = $true
        }
    }
    
    if (-not $skip) {
        $output += $line
    }
    
    if ($skip -and $line -match '</PropertyGroup>') {
        $skip = $false
    }
}

$output | Set-Content $file -Encoding UTF8
Write-Host "Removed OutDir/IntDir PropertyGroup from BackupEngine.vcxproj"
