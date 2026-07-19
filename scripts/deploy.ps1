$CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$CurrentPrincipal = [Security.Principal.WindowsPrincipal]::new($CurrentIdentity)

$IsAdmin = $CurrentPrincipal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $IsAdmin) {
    Write-Host "Requesting Administrator permission..." -ForegroundColor Yellow

    $ScriptPath = $PSCommandPath
    $Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""

    Start-Process `
        -FilePath "powershell.exe" `
        -Verb RunAs `
        -ArgumentList $Arguments `
        -WorkingDirectory "C:\Projects\MaximoROWeb"

    return
}

$ErrorActionPreference = "Stop"

$TaskName = "MaximoROWeb"
$ProjectRoot = "C:\Projects\MaximoROWeb"
$Port = 5041

Set-Location $ProjectRoot

Write-Host "Stopping any running production task..." -ForegroundColor Yellow

Stop-ScheduledTask `
    -TaskName $TaskName `
    -ErrorAction SilentlyContinue

Start-Sleep -Seconds 2

$Listener = Get-NetTCPConnection `
    -LocalPort $Port `
    -State Listen `
    -ErrorAction SilentlyContinue |
Select-Object -First 1

if ($Listener) {
    throw "Port $Port is still in use by PID $($Listener.OwningProcess). Stop dotnet watch with Ctrl+C before deploying."
}

Write-Host "Publishing MaximoROWeb..." -ForegroundColor Cyan

dotnet publish `
    ".\MaximoROWeb.csproj" `
    -c Release `
    -o ".\publish"

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Enable-ScheduledTask `
    -TaskName $TaskName `
    -ErrorAction SilentlyContinue |
Out-Null

Start-ScheduledTask -TaskName $TaskName

Start-Sleep -Seconds 8

$Response = Invoke-WebRequest `
    "http://127.0.0.1:5041/" `
    -UseBasicParsing `
    -TimeoutSec 10

Write-Host "MaximoROWeb deployed successfully." -ForegroundColor Green
Write-Host "HTTP status: $($Response.StatusCode)" -ForegroundColor Green