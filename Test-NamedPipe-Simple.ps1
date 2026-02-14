# Simple named pipe test with timeout
# Tests if the service responds to GetVersion command

$pipeName = "BackupRestoreServicePipe"
$timeout = 5000 # 5 seconds

Write-Host "Testing named pipe communication..." -ForegroundColor Cyan

try {
    # Create pipe client
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    
    Write-Host "1. Connecting to pipe..." -ForegroundColor Yellow
    $pipe.Connect($timeout)
    Write-Host "   ? Connected" -ForegroundColor Green
    
    # Create streams
    $writer = New-Object System.IO.StreamWriter($pipe)
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($pipe)
    
    # Send GetVersion command
    $command = @{
        CommandType = "GetVersion"
        Data = $null
    } | ConvertTo-Json -Compress
    
    Write-Host "2. Sending GetVersion command..." -ForegroundColor Yellow
    Write-Host "   Command: $command" -ForegroundColor Gray
    $writer.WriteLine($command)
    
    Write-Host "3. Waiting for response (5 second timeout)..." -ForegroundColor Yellow
    
    # Read with timeout
    $task = $reader.ReadLineAsync()
    if ($task.Wait($timeout)) {
        $response = $task.Result
        Write-Host "   ? Response received!" -ForegroundColor Green
        Write-Host "   Response: $response" -ForegroundColor White
        
        # Parse response
        $responseObj = $response | ConvertFrom-Json
        if ($responseObj.Success) {
            Write-Host "`n? SERVICE VERSION: $($responseObj.Message)" -ForegroundColor Green -BackgroundColor Black
        } else {
            Write-Host "`n? ERROR: $($responseObj.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "   ? TIMEOUT - No response after 5 seconds" -ForegroundColor Red
        Write-Host "   Service is running but not responding to GetVersion command" -ForegroundColor Yellow
    }
    
    $writer.Close()
    $reader.Close()
    $pipe.Close()
}
catch {
    Write-Host "`n? ERROR: $_" -ForegroundColor Red
    Write-Host "Stack: $($_.ScriptStackTrace)" -ForegroundColor Gray
}
finally {
    if ($pipe) { $pipe.Dispose() }
}
