$ErrorActionPreference = "Stop"

# Resolve the script path even when VS Code dot-sources the file.
$ScriptPath = $PSCommandPath

if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
    $ScriptPath = $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
    throw "PowerShell could not determine the path to dev.ps1."
}

$CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$CurrentPrincipal = [Security.Principal.WindowsPrincipal]::new($CurrentIdentity)

$IsAdmin = $CurrentPrincipal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $IsAdmin) {
    Write-Host "Requesting Administrator permission..." -ForegroundColor Yellow

    $PowerShellPath = Join-Path `
        $PSHOME `
        "powershell.exe"

    $EscapedScriptPath = $ScriptPath.Replace("'", "''")

    $ElevatedArguments = @(
        "-NoExit"
        "-NoProfile"
        "-ExecutionPolicy Bypass"
        "-Command `"& '$EscapedScriptPath'`""
    ) -join " "

    try {
        Start-Process `
            -FilePath $PowerShellPath `
            -Verb RunAs `
            -ArgumentList $ElevatedArguments `
            -WorkingDirectory "C:\Projects\MaximoROWeb" `
            -WindowStyle Normal `
            -ErrorAction Stop
    }
    catch {
        Write-Host "Administrator launch failed:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }

    # Use return because VS Code runs the file by dot-sourcing it.
    return
}

$TaskName = "MaximoROWeb"
$Port = 5041
$ProjectRoot = "C:\Projects\MaximoROWeb"

Write-Host "Entering MaximoROWeb development mode..." -ForegroundColor Cyan

Disable-ScheduledTask `
    -TaskName $TaskName `
    -ErrorAction SilentlyContinue |
Out-Null

Stop-ScheduledTask `
    -TaskName $TaskName `
    -ErrorAction SilentlyContinue

Start-Sleep -Seconds 2

$Deadline = (Get-Date).AddSeconds(15)

do {
    $Listeners = @(
        Get-NetTCPConnection `
            -LocalPort $Port `
            -State Listen `
            -ErrorAction SilentlyContinue
    )

    foreach ($Listener in $Listeners) {
        $OwningProcessId = $Listener.OwningProcess

        Write-Host "Stopping PID $OwningProcessId on port $Port..." `
            -ForegroundColor Yellow

        try {
            Stop-Process `
                -Id $OwningProcessId `
                -Force `
                -ErrorAction Stop
        }
        catch {
            throw "Could not stop PID $OwningProcessId. $($_.Exception.Message)"
        }
    }

    if ($Listeners.Count -eq 0) {
        break
    }

    Start-Sleep -Milliseconds 500
}
while ((Get-Date) -lt $Deadline)

$RemainingListener = Get-NetTCPConnection `
    -LocalPort $Port `
    -State Listen `
    -ErrorAction SilentlyContinue |
Select-Object -First 1

if ($RemainingListener) {
    throw "Port $Port is still occupied by PID $($RemainingListener.OwningProcess)."
}

Set-Location $ProjectRoot

Write-Host "Starting dotnet watch on http://127.0.0.1:5041..." `
    -ForegroundColor Green

& dotnet watch run --urls "http://127.0.0.1:5041"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet watch exited with code $LASTEXITCODE."
}