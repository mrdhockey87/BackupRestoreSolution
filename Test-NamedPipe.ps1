# Test Named Pipe Communication with BackupRestoreService
$pipeName = "BackupRestoreServicePipe"

Write-Host "=== Named Pipe Communication Test ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check if service is running
Write-Host "1. Checking service status..." -ForegroundColor Yellow
$service = Get-Service -Name BackupRestoreService -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "   Service Status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Red' })
} else {
    Write-Host "   ERROR: Service not found!" -ForegroundColor Red
    exit 1
}

if ($service.Status -ne 'Running') {
    Write-Host "   ERROR: Service is not running. Run Reinstall-Service.ps1" -ForegroundColor Red
    exit 1
}

# 2. Check if named pipe exists
Write-Host "`n2. Checking for named pipe..." -ForegroundColor Yellow
$pipes = [System.IO.Directory]::GetFiles("\\.\pipe\")
$ourPipe = $pipes | Where-Object { $_ -like "*BackupRestore*" }
if ($ourPipe) {
    Write-Host "   Found pipe: $ourPipe" -ForegroundColor Green
} else {
    Write-Host "   WARNING: Named pipe not found in pipe directory" -ForegroundColor Yellow
    Write-Host "   This might mean the service hasn't created it yet or failed to start pipe listener" -ForegroundColor Yellow
}

# 3. Try to connect
Write-Host "`n3. Attempting to connect to pipe..." -ForegroundColor Yellow
try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    Write-Host "   Connecting (5 second timeout)..." -ForegroundColor Gray
    
    $pipe.Connect(5000)
    Write-Host "   Connected successfully!" -ForegroundColor Green
    
    # 4. Send GetVersion command
    Write-Host "`n4. Sending GetVersion command..." -ForegroundColor Yellow
    
    $encoding = [System.Text.Encoding]::UTF8
    $writer = New-Object System.IO.StreamWriter($pipe, $encoding, -1, $true)
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($pipe, $encoding, $true, -1, $true)
    
    $command = @{
        CommandType = "GetVersion"
        Data = $null
    } | ConvertTo-Json -Compress
    
    Write-Host "   Command JSON: $command" -ForegroundColor Gray
    $writer.WriteLine($command)
    $writer.Flush()
    
    # 5. Read response
    Write-Host "`n5. Reading response..." -ForegroundColor Yellow
    $response = $reader.ReadLine()
    
    if ($response) {
        Write-Host "   Raw response: $response" -ForegroundColor Gray
        
        try {
            $responseObj = $response | ConvertFrom-Json
            Write-Host "   Success: $($responseObj.Success)" -ForegroundColor Green
            Write-Host "   Message: $($responseObj.Message)" -ForegroundColor Green
        } catch {
            Write-Host "   Response is not JSON: $response" -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ERROR: No response received!" -ForegroundColor Red
    }
    
    $writer.Dispose()
    $reader.Dispose()
    $pipe.Close()
    
    Write-Host "`n=== Test Complete ===" -ForegroundColor Green
}
catch {
    Write-Host "   ERROR: $_" -ForegroundColor Red
    Write-Host "   Exception Type: $($_.Exception.GetType().FullName)" -ForegroundColor Red
    Write-Host "   Stack Trace:" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Gray
    
    if ($_.Exception.InnerException) {
        Write-Host "   Inner Exception: $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
    
    Write-Host "`n=== Test Failed ===" -ForegroundColor Red
}

Write-Host "`nTo reinstall service with latest code, run:" -ForegroundColor Cyan
Write-Host "   .\Reinstall-Service.ps1" -ForegroundColor White
