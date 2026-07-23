$ErrorActionPreference = "Stop"

# ============================================================
# MaximoROWeb deployment configuration
# ============================================================

$Root = "C:\Projects\MaximoROWeb"
$ProjectName = "MaximoROWeb"
$LivePort = 5043
$PublicUrl = "https://desktop-68ka5hg.tail9fc6cc.ts.net:8443"

$PublishFolder = Join-Path $Root "publish-5043"
$StagingFolder = "C:\Projects\MaximoROWeb-PublishStaging"
$BackupRoot = "C:\Projects\MaximoROWeb-Backups"
$OutputFolder = "C:\Users\Maximo\Desktop\MaximoRO Outputs"
$StartScript = Join-Path $Root "scripts\start-maximoroweb.ps1"

$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$BackupFolder = Join-Path $BackupRoot "publish-before-deploy-$Timestamp"
$Report = Join-Path $OutputFolder "maximoroweb-publish-deploy_$Timestamp.txt"

$HealthUrl = "http://127.0.0.1:$LivePort/"
$RegisterUrl = "http://127.0.0.1:$LivePort/Home/Register"
$PublicRegisterUrl = "$PublicUrl/Home/Register"

New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

if ((Get-Location).Path.StartsWith($PublishFolder, [System.StringComparison]::OrdinalIgnoreCase)) {
    Set-Location $Root
}

$Changes = [System.Collections.Generic.List[string]]::new()
$Warnings = [System.Collections.Generic.List[string]]::new()
$PublishOutput = @()
$Checks = @()
$StartedProcesses = @()
$BackupCreated = $false
$NewPublishInstalled = $false

# ============================================================
# Helper functions
# ============================================================

function Get-MaximoROWebConnection {
    Get-NetTCPConnection `
        -LocalPort $LivePort `
        -State Listen `
        -ErrorAction SilentlyContinue |
    Select-Object -First 1
}

function Get-ProcessInfo {
    param([int]$ProcessId)

    Get-CimInstance `
        Win32_Process `
        -Filter "ProcessId = $ProcessId" `
        -ErrorAction SilentlyContinue
}

function Stop-MaximoROWebProcess {
    param([switch]$RequireStopped)

    $Connection = Get-MaximoROWebConnection

    if (-not $Connection) {
        return
    }

    $ProcessId = [int]$Connection.OwningProcess
    $ProcessInfo = Get-ProcessInfo -ProcessId $ProcessId

    if (-not $ProcessInfo) {
        throw "Port $LivePort is occupied, but its process could not be inspected."
    }

    $CommandLine = [string]$ProcessInfo.CommandLine
    $ProcessName = [string]$ProcessInfo.Name
    $ExecutablePath = [string]$ProcessInfo.ExecutablePath
    $ExpectedRoot = [regex]::Escape($Root)

    $LooksLikeMaximoROWeb =
        $ProcessName -eq "$ProjectName.exe" -or
        $CommandLine -match $ProjectName -or
        $CommandLine -match $ExpectedRoot -or
        $ExecutablePath -match $ProjectName

    if (-not $LooksLikeMaximoROWeb) {
        throw "Port $LivePort belongs to an unexpected process: $CommandLine"
    }

    $ParentProcessId = [int]$ProcessInfo.ParentProcessId
    $ParentInfo = Get-ProcessInfo -ProcessId $ParentProcessId

    $ProcessIdsToStop = [System.Collections.Generic.List[int]]::new()
    $ProcessIdsToStop.Add($ProcessId)

    if ($ParentInfo) {
        $ParentCommandLine = [string]$ParentInfo.CommandLine
        $ParentName = [string]$ParentInfo.Name

        if (
            $ParentName -in @("dotnet.exe", "powershell.exe", "pwsh.exe") -and
            ($ParentCommandLine -match $ProjectName -or $ParentCommandLine -match [regex]::Escape($StartScript))
        ) {
            $ProcessIdsToStop.Add($ParentProcessId)
        }
    }

    foreach ($Id in ($ProcessIdsToStop | Select-Object -Unique)) {
        try {
            Stop-Process -Id $Id -Force -ErrorAction Stop
            Wait-Process -Id $Id -Timeout 15 -ErrorAction SilentlyContinue
            $Changes.Add("Stopped MaximoROWeb process PID $Id.")
        }
        catch {
            if ($RequireStopped) {
                throw "Could not stop PID $Id on port ${LivePort}: $($_.Exception.Message)"
            }

            $Warnings.Add("Could not stop PID $Id on port ${LivePort}: $($_.Exception.Message)")
        }
    }

    Start-Sleep -Seconds 2

    $Deadline = (Get-Date).AddSeconds(20)

    do {
        Start-Sleep -Milliseconds 500
        $StillListening = Get-MaximoROWebConnection
    }
    until (-not $StillListening -or (Get-Date) -ge $Deadline)

    if ($StillListening -and $RequireStopped) {
        throw "MaximoROWeb did not release port $LivePort."
    }
}

function Wait-ForPathUnlock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [int]$TimeoutSeconds = 30
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $Deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        try {
            $Stream = [System.IO.File]::Open(
                $Path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            $Stream.Dispose()
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    until ((Get-Date) -ge $Deadline)

    throw "Timed out waiting for file to unlock: $Path"
}

function Start-MaximoROWebProcess {
    if (-not (Test-Path $StartScript)) {
        throw "Start script was not found: $StartScript"
    }

    $Process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", $StartScript
        ) `
        -WorkingDirectory $Root `
        -WindowStyle Hidden `
        -PassThru

    $StartedProcesses += $Process.Id
    $Changes.Add("Started MaximoROWeb with launcher PID $($Process.Id) on port $LivePort.")

    $Deadline = (Get-Date).AddSeconds(45)
    $Connection = $null

    do {
        Start-Sleep -Seconds 2
        $Connection = Get-MaximoROWebConnection
    }
    until ($Connection -or (Get-Date) -ge $Deadline)

    if (-not $Connection) {
        throw "MaximoROWeb did not begin listening on port $LivePort."
    }

    return $Connection
}

function Test-MaximoROWeb {
    $Urls = @(
        $HealthUrl,
        $RegisterUrl,
        $PublicRegisterUrl
    )

    foreach ($Url in $Urls) {
        try {
            $Response = Invoke-WebRequest `
                -Uri $Url `
                -UseBasicParsing `
                -TimeoutSec 15

            $Content = [string]$Response.Content
            $ExpectedContent = $true

            if ($Url -like "*/Home/Register") {
                $ExpectedContent =
                    $Content.Contains("Create Account") -and
                    $Content.Contains("type=`"submit`"") -and
                    -not $Content.Contains("Registration Temporarily Unavailable")
            }

            [pscustomobject]@{
                Url = $Url
                Status = $Response.StatusCode
                ExpectedContent = $ExpectedContent
                Result = if ($ExpectedContent) { "OK" } else { "Registration form is unavailable or SMTP configuration is incomplete" }
            }
        }
        catch {
            [pscustomobject]@{
                Url = $Url
                Status = "ERROR"
                ExpectedContent = $false
                Result = $_.Exception.Message
            }
        }
    }
}

function Write-DeploymentReport {
    param(
        [string]$Result,
        [string]$FailureMessage = ""
    )

    $Connection = Get-MaximoROWebConnection
    $LiveProcess = $null

    if ($Connection) {
        $LiveProcess = Get-ProcessInfo -ProcessId ([int]$Connection.OwningProcess)
    }

    & {
        Write-Output "MaximoROWeb Deployment"
        Write-Output "Generated: $(Get-Date)"
        Write-Output ""
        Write-Output "=== Result ==="
        Write-Output $Result

        if ($FailureMessage) {
            Write-Output "Failure: $FailureMessage"
        }

        Write-Output ""
        Write-Output "=== Configuration ==="
        Write-Output "Root: $Root"
        Write-Output "Publish: $PublishFolder"
        Write-Output "Local URL: $HealthUrl"
        Write-Output "Public URL: $PublicUrl"

        Write-Output ""
        Write-Output "=== Changes ==="
        if ($Changes.Count -eq 0) {
            Write-Output "No completed changes were recorded."
        }
        else {
            foreach ($Change in $Changes) {
                Write-Output "[OK] $Change"
            }
        }

        if ($Warnings.Count -gt 0) {
            Write-Output ""
            Write-Output "=== Warnings ==="
            foreach ($Warning in $Warnings) {
                Write-Output "[WARNING] $Warning"
            }
        }

        Write-Output ""
        Write-Output "=== Live Process ==="
        if ($LiveProcess) {
            Write-Output "PID: $($LiveProcess.ProcessId)"
            Write-Output "Command: $($LiveProcess.CommandLine)"
        }
        else {
            Write-Output "Nothing is listening on port $LivePort."
        }

        Write-Output ""
        Write-Output "=== HTTP Checks ==="
        if ($Checks.Count -gt 0) {
            $Checks |
            Format-Table -AutoSize |
            Out-String -Width 260 |
            Write-Output
        }
        else {
            Write-Output "No HTTP checks were completed."
        }

        Write-Output ""
        Write-Output "=== Publish Output ==="
        if ($PublishOutput.Count -gt 0) {
            $PublishOutput
        }
        else {
            Write-Output "No publish output was captured."
        }

        Write-Output ""
        Write-Output "=== Backup ==="
        if ($BackupCreated -and (Test-Path $BackupFolder)) {
            Write-Output $BackupFolder
        }
        else {
            Write-Output "No deployment backup was created."
        }
    } 2>&1 | Tee-Object -FilePath $Report
}

function Restore-PreviousPublish {
    if (-not ($BackupCreated -and (Test-Path $BackupFolder))) {
        if ($NewPublishInstalled) {
            $Warnings.Add("No previous publish backup existed, so there was nothing to restore.")
        }

        return
    }

    $LiveDll = Join-Path $PublishFolder "$ProjectName.dll"
    Wait-ForPathUnlock -Path $LiveDll

    if (Test-Path $PublishFolder) {
        Remove-Item $PublishFolder -Recurse -Force
    }

    New-Item -ItemType Directory -Path $PublishFolder -Force | Out-Null
    Copy-Item -Path (Join-Path $BackupFolder "*") -Destination $PublishFolder -Recurse -Force
    $Changes.Add("Restored the previous deployment from backup.")
}

# ============================================================
# Deployment
# ============================================================

try {
    Write-Host ""
    Write-Host "=== MaximoROWeb Deployment ===" -ForegroundColor Cyan
    Write-Host "Deploying $Root to local port $LivePort"

    $ProjectFile = Get-ChildItem -Path $Root -Filter "*.csproj" -File | Select-Object -First 1

    if (-not $ProjectFile) {
        throw "No project file was found in $Root."
    }

    if (Test-Path $StagingFolder) {
        Remove-Item $StagingFolder -Recurse -Force
    }

    New-Item -ItemType Directory -Path $StagingFolder -Force | Out-Null

    Write-Host "Publishing to staging..."
    Push-Location $Root

    try {
        $PublishOutput = @(
            & dotnet publish `
                $ProjectFile.FullName `
                -c Release `
                -o $StagingFolder `
                --no-self-contained `
                /p:UseAppHost=false `
                2>&1
        )

        $PublishExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($PublishExitCode -ne 0) {
        throw "dotnet publish failed with exit code $PublishExitCode."
    }

    $PublishedDll = Join-Path $StagingFolder "$ProjectName.dll"

    if (-not (Test-Path $PublishedDll)) {
        throw "Staging output is missing $ProjectName.dll."
    }

    $Changes.Add("Published the project successfully to staging.")

    Write-Host "Stopping live MaximoROWeb process on port $LivePort..."
    Stop-MaximoROWebProcess -RequireStopped

    if (Test-Path $PublishFolder) {
        New-Item -ItemType Directory -Path $BackupFolder -Force | Out-Null
        Copy-Item -Path (Join-Path $PublishFolder "*") -Destination $BackupFolder -Recurse -Force
        $BackupCreated = $true
        $Changes.Add("Backed up the previous live deployment.")
    }

    $LiveDll = Join-Path $PublishFolder "$ProjectName.dll"
    Wait-ForPathUnlock -Path $LiveDll

    if (Test-Path $PublishFolder) {
        Remove-Item $PublishFolder -Recurse -Force
    }

    Move-Item -Path $StagingFolder -Destination $PublishFolder
    $NewPublishInstalled = $true
    $Changes.Add("Promoted staging to the live publish folder.")

    Write-Host "Starting MaximoROWeb on port $LivePort..."
    $LiveConnection = Start-MaximoROWebProcess

    Write-Host "Running health checks..."
    $Checks = @(Test-MaximoROWeb)

    $FailedChecks = $Checks | Where-Object { $_.Status -ne 200 -or -not $_.ExpectedContent }

    if ($FailedChecks) {
        throw "One or more HTTP health checks failed."
    }

    $Changes.Add("Local and public HTTP health checks passed.")

    Write-DeploymentReport -Result "SUCCESS"

    Write-Host ""
    Write-Host "Deployment completed successfully." -ForegroundColor Green
    Write-Host "Report:"
    Write-Host $Report
}
catch {
    $FailureMessage = $_.Exception.Message
    $Warnings.Add($FailureMessage)

    Write-Host ""
    Write-Host "Deployment failed. Beginning rollback..." -ForegroundColor Red

    try {
        Stop-MaximoROWebProcess
    }
    catch {
        $Warnings.Add("Could not completely stop the failed deployment: $($_.Exception.Message)")
    }

    try {
        Restore-PreviousPublish
    }
    catch {
        $Warnings.Add("Rollback copy failed: $($_.Exception.Message)")
    }

    try {
        $RollbackConnection = Start-MaximoROWebProcess
        $Checks = @(Test-MaximoROWeb)

        $RollbackFailedChecks = $Checks | Where-Object { $_.Status -ne 200 }

        if ($RollbackFailedChecks) {
            $Warnings.Add("The rollback process started, but an HTTP health check still failed.")
        }
        else {
            $Changes.Add("Rollback deployment is online.")
        }
    }
    catch {
        $Warnings.Add("Could not restart MaximoROWeb after rollback: $($_.Exception.Message)")
    }

    Write-DeploymentReport `
        -Result "FAILED - ROLLBACK ATTEMPTED" `
        -FailureMessage $FailureMessage

    Write-Host ""
    Write-Host "Deployment failed." -ForegroundColor Red
    Write-Host $FailureMessage
    Write-Host "Report:"
    Write-Host $Report

    exit 1
}
