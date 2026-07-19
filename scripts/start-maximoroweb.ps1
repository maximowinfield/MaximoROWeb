$ErrorActionPreference = "Continue"

$ProjectRoot = "C:\Projects\MaximoROWeb"
$PublishPath = Join-Path $ProjectRoot "publish"
$LogDirectory = Join-Path $ProjectRoot "logs"
$LogPath = Join-Path $LogDirectory "website.log"
$ApplicationDll = Join-Path $PublishPath "MaximoROWeb.dll"

New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

function Write-WebsiteLog {
    param([string]$Message)

    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" |
        Out-File -FilePath $LogPath -Append
}

if (-not (Test-Path $ApplicationDll)) {
    Write-WebsiteLog "Published application was not found: $ApplicationDll"
    exit 1
}

Set-Location $PublishPath

$env:ASPNETCORE_ENVIRONMENT = "Production"

Write-WebsiteLog "MaximoROWeb supervisor started."

while ($true) {
    try {
        Write-WebsiteLog "Starting MaximoROWeb."

        & dotnet $ApplicationDll `
            --urls "http://127.0.0.1:5041" `
            *>> $LogPath

        $ExitCode = $LASTEXITCODE

        Write-WebsiteLog "MaximoROWeb exited with code $ExitCode."
    }
    catch {
        Write-WebsiteLog "Launcher error: $($_.Exception.Message)"
    }

    Write-WebsiteLog "Restarting MaximoROWeb in 10 seconds."
    Start-Sleep -Seconds 10
}
