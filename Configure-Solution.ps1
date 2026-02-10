# Configure-Solution.ps1
# Configures BackupRestoreSolution with proper build order and LinuxRestore folder

Write-Host "Configuring BackupRestoreSolution..." -ForegroundColor Green

$solutionFile = "BackupRestoreSolution.sln"

# Read current solution
$content = Get-Content $solutionFile -Raw

# Add LinuxRestore as solution folder if not exists
if ($content -notmatch "LinuxRestore") {
    Write-Host "Adding LinuxRestore solution folder..." -ForegroundColor Yellow
    
    $linuxRestoreSection = @"
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "LinuxRestore", "LinuxRestore", "{F1E2D3C4-B5A6-7980-CDEF-123456789ABC}"
	ProjectSection(SolutionItems) = preProject
		LinuxRestore\BUILD-AND-CREATE-ISO.ps1 = LinuxRestore\BUILD-AND-CREATE-ISO.ps1
		LinuxRestore\CMakeLists.txt = LinuxRestore\CMakeLists.txt
		LinuxRestore\create_bootable_usb.sh = LinuxRestore\create_bootable_usb.sh
		LinuxRestore\README.md = LinuxRestore\README.md
		LinuxRestore\restore_cli.cpp = LinuxRestore\restore_cli.cpp
		LinuxRestore\restore_engine.cpp = LinuxRestore\restore_engine.cpp
		LinuxRestore\restore_gui_gtk.cpp = LinuxRestore\restore_gui_gtk.cpp
		LinuxRestore\restore_tui.cpp = LinuxRestore\restore_tui.cpp
		LinuxRestore\UPDATE_5.11.0.7.md = LinuxRestore\UPDATE_5.11.0.7.md
	EndProjectSection
EndProject
"@
    
    # Insert before Global section
    $content = $content -replace "(Global)", "$linuxRestoreSection`r`n`$1"
}

# Add project dependencies if not exists
if ($content -notmatch "ProjectDependencies") {
    Write-Host "Adding project dependencies for build order..." -ForegroundColor Yellow
    
    # BackupService depends on BackupEngine
    $content = $content -replace `
        '(Project\("\{9A19103F-16F7-4668-BE54-9A1E7A4F7556\}"\) = "BackupService"[^\r\n]+\r?\n)(EndProject)', `
        "`$1`tProjectSection(ProjectDependencies) = postProject`r`n`t`t{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942} = {8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}`r`n`tEndProjectSection`r`n`$2"
    
    # BackupUI depends on BackupEngine and BackupService
    $content = $content -replace `
        '(Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "BackupUI"[^\r\n]+\r?\n)(EndProject)', `
        "`$1`tProjectSection(ProjectDependencies) = postProject`r`n`t`t{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942} = {8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}`r`n`t`t{C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F} = {C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F}`r`n`tEndProjectSection`r`n`$2"
}

# Write updated solution
Set-Content -Path $solutionFile -Value $content -NoNewline

Write-Host "Solution configured successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Build Order:" -ForegroundColor Cyan
Write-Host "  1. BackupEngine (C++ DLL)" -ForegroundColor White
Write-Host "  2. BackupService (Windows Service)" -ForegroundColor White
Write-Host "  3. BackupUI (WPF Application)" -ForegroundColor White
Write-Host ""
Write-Host "LinuxRestore:" -ForegroundColor Cyan
Write-Host "  - Added as solution folder" -ForegroundColor White
Write-Host "  - Not built with Windows projects" -ForegroundColor White
Write-Host "  - Build manually with BUILD-AND-CREATE-ISO.ps1" -ForegroundColor White
