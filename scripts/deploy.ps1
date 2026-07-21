$ErrorActionPreference = "Stop"

# ============================================================
# MaximoROWeb deployment configuration
# ============================================================

$Root = "C:\Projects\MaximoROweb"
$TaskName = "MaximoROWeb"
$TaskPath = "\"

$PublishFolder = Join-Path $Root "publish"
$StagingFolder = Join-Path $Root "publish-staging"
$BackupRoot = Join-Path $Root "Backups"

$OutputFolder = "C:\Users\Maximo\Desktop\MaximoRO Outputs"
$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"

$BackupFolder = Join-Path `
    $BackupRoot `
    "publish-before-deploy-$Timestamp"

$Report = Join-Path `
    $OutputFolder `
    "maximoroweb-publish-deploy_$Timestamp.txt"

$HealthUrl = "http://127.0.0.1:5041/"
$ProgressionUrl = "http://127.0.0.1:5041/Home/ProgressionPatch"

New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

# Never remain inside publish while trying to replace it.
$CurrentFolder = (Get-Location).Path

if (
    $CurrentFolder -eq $PublishFolder -or
    $CurrentFolder.StartsWith(
        "$PublishFolder\",
        [System.StringComparison]::OrdinalIgnoreCase
    )
) {
    Set-Location $Root
}

$Changes = [System.Collections.Generic.List[string]]::new()
$Warnings = [System.Collections.Generic.List[string]]::new()

$PublishOutput = @()
$Checks = @()

$TaskWasEnabled = $false
$BackupCreated = $false
$NewPublishInstalled = $false

# ============================================================
# Helper functions
# ============================================================

function Get-MaximoROWebConnection {
    Get-NetTCPConnection `
        -LocalPort 5041 `
        -State Listen `
        -ErrorAction SilentlyContinue |
    Select-Object -First 1
}

function Stop-MaximoROWebProcess {
    $Connection = Get-MaximoROWebConnection

    if (-not $Connection) {
        return
    }

    $ProcessId = $Connection.OwningProcess

    $ProcessInfo = Get-CimInstance `
        Win32_Process `
        -Filter "ProcessId = $ProcessId" `
        -ErrorAction SilentlyContinue

    if (-not $ProcessInfo) {
        throw "Port 5041 is occupied, but its process could not be inspected."
    }

    $CommandLine = [string]$ProcessInfo.CommandLine
    $ExecutablePath = [string]$ProcessInfo.ExecutablePath

    if (
        $CommandLine -notmatch "MaximoROWeb" -and
        $CommandLine -notmatch [regex]::Escape($Root) -and
        $ExecutablePath -notmatch "dotnet"
    ) {
        throw "Port 5041 belongs to an unexpected process: $CommandLine"
    }

    Stop-Process `
        -Id $ProcessId `
        -Force `
        -ErrorAction SilentlyContinue

    $Deadline = (Get-Date).AddSeconds(20)

    do {
        Start-Sleep -Milliseconds 500
        $StillListening = Get-MaximoROWebConnection
    }
    until (
        -not $StillListening -or
        (Get-Date) -ge $Deadline
    )

    if ($StillListening) {
        throw "MaximoROWeb did not release port 5041."
    }

    $Changes.Add("Stopped MaximoROWeb process PID $ProcessId.")
}

function Start-MaximoROWebTask {
    Enable-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction Stop |
    Out-Null

    Start-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction Stop

    $Deadline = (Get-Date).AddSeconds(45)
    $Connection = $null

    do {
        Start-Sleep -Seconds 2
        $Connection = Get-MaximoROWebConnection
    }
    until (
        $Connection -or
        (Get-Date) -ge $Deadline
    )

    if (-not $Connection) {
        $TaskInfo = Get-ScheduledTaskInfo `
            -TaskName $TaskName `
            -ErrorAction SilentlyContinue

        throw (
            "MaximoROWeb did not begin listening on port 5041. " +
            "Scheduled Task result: $($TaskInfo.LastTaskResult)"
        )
    }

    return $Connection
}

function Test-MaximoROWeb {
    $Urls = @(
        $HealthUrl,
        $ProgressionUrl
    )

    $Results = foreach ($Url in $Urls) {
        try {
            $Response = Invoke-WebRequest `
                -Uri $Url `
                -UseBasicParsing `
                -TimeoutSec 15

            [pscustomobject]@{
                Url    = $Url
                Status = $Response.StatusCode
                Result = "OK"
            }
        }
        catch {
            [pscustomobject]@{
                Url    = $Url
                Status = "ERROR"
                Result = $_.Exception.Message
            }
        }
    }

    return $Results
}

function Write-DeploymentReport {
    param(
        [string]$Result,
        [string]$FailureMessage = ""
    )

    $Task = Get-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction SilentlyContinue

    $TaskInfo = Get-ScheduledTaskInfo `
        -TaskName $TaskName `
        -ErrorAction SilentlyContinue

    $Connection = Get-MaximoROWebConnection
    $LiveProcess = $null

    if ($Connection) {
        $LiveProcess = Get-CimInstance `
            Win32_Process `
            -Filter "ProcessId = $($Connection.OwningProcess)" `
            -ErrorAction SilentlyContinue
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
        Write-Output "=== Scheduled Task ==="
        Write-Output "Task: $TaskName"
        Write-Output "State: $($Task.State)"
        Write-Output "Last run: $($TaskInfo.LastRunTime)"
        Write-Output "Last result: $($TaskInfo.LastTaskResult)"

        Write-Output ""
        Write-Output "=== Live Process ==="

        if ($LiveProcess) {
            Write-Output "PID: $($LiveProcess.ProcessId)"
            Write-Output "Command: $($LiveProcess.CommandLine)"
        }
        else {
            Write-Output "Nothing is listening on port 5041."
        }

        Write-Output ""
        Write-Output "=== HTTP Checks ==="

        if ($Checks.Count -gt 0) {
            $Checks |
            Format-Table -AutoSize |
            Out-String -Width 240 |
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

# ============================================================
# Deployment
# ============================================================

try {
    Write-Host ""
    Write-Host "=== MaximoROWeb Deployment ===" -ForegroundColor Cyan

    $ProjectFile = Get-ChildItem `
        -Path $Root `
        -Filter "*.csproj" `
        -File |
    Select-Object -First 1

    if (-not $ProjectFile) {
        throw "No project file was found in $Root."
    }

    $Task = Get-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction Stop

    $TaskWasEnabled = $Task.State -ne "Disabled"

    # --------------------------------------------------------
    # Publish first, while the current site remains online
    # --------------------------------------------------------

    if (Test-Path $StagingFolder) {
        Remove-Item `
            $StagingFolder `
            -Recurse `
            -Force
    }

    New-Item `
        -ItemType Directory `
        -Path $StagingFolder `
        -Force |
    Out-Null

    Write-Host "Publishing to staging..."

    Push-Location $Root

    try {
        $PublishOutput = @(
            & dotnet publish `
                $ProjectFile.FullName `
                -c Release `
                -o $StagingFolder `
                --no-self-contained `
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

    $PublishedDll = Join-Path `
        $StagingFolder `
        "MaximoROWeb.dll"

    if (-not (Test-Path $PublishedDll)) {
        throw "Staging output is missing MaximoROWeb.dll."
    }

    $Changes.Add("Published the project successfully to staging.")

    # --------------------------------------------------------
    # Stop Task Scheduler and live process
    # --------------------------------------------------------

    Write-Host "Stopping and disabling Scheduled Task..."

    Disable-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction Stop |
    Out-Null

    Stop-ScheduledTask `
        -TaskName $TaskName `
        -ErrorAction SilentlyContinue

    Start-Sleep -Seconds 2

    Stop-MaximoROWebProcess

    # --------------------------------------------------------
    # Back up current deployment
    # --------------------------------------------------------

    if (Test-Path $PublishFolder) {
        New-Item `
            -ItemType Directory `
            -Path $BackupFolder `
            -Force |
        Out-Null

        Copy-Item `
            -Path (Join-Path $PublishFolder "*") `
            -Destination $BackupFolder `
            -Recurse `
            -Force

        $BackupCreated = $true
        $Changes.Add("Backed up the previous live deployment.")
    }

    # --------------------------------------------------------
    # Replace live deployment
    # --------------------------------------------------------

    if (Test-Path $PublishFolder) {
        Remove-Item `
            $PublishFolder `
            -Recurse `
            -Force
    }

    Move-Item `
        -Path $StagingFolder `
        -Destination $PublishFolder

    $NewPublishInstalled = $true
    $Changes.Add("Promoted staging to the live publish folder.")

    # --------------------------------------------------------
    # Start site through Task Scheduler
    # --------------------------------------------------------

    Write-Host "Starting MaximoROWeb Scheduled Task..."

    $LiveConnection = Start-MaximoROWebTask
    $Changes.Add("Scheduled Task started MaximoROWeb on port 5041.")

    # --------------------------------------------------------
    # Health checks
    # --------------------------------------------------------

    $Checks = @(Test-MaximoROWeb)

    $FailedChecks = $Checks |
    Where-Object {
        $_.Status -ne 200
    }

    if ($FailedChecks) {
        throw "One or more local HTTP health checks failed."
    }

    $Changes.Add("Local HTTP health checks passed.")

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
        Disable-ScheduledTask `
            -TaskName $TaskName `
            -ErrorAction SilentlyContinue |
        Out-Null

        Stop-ScheduledTask `
            -TaskName $TaskName `
            -ErrorAction SilentlyContinue

        Start-Sleep -Seconds 2

        Stop-MaximoROWebProcess
    }
    catch {
        $Warnings.Add(
            "Could not completely stop the failed deployment: " +
            $_.Exception.Message
        )
    }

    # --------------------------------------------------------
    # Restore previous live publish
    # --------------------------------------------------------

    if ($BackupCreated -and (Test-Path $BackupFolder)) {
        try {
            if (Test-Path $PublishFolder) {
                Remove-Item `
                    $PublishFolder `
                    -Recurse `
                    -Force
            }

            New-Item `
                -ItemType Directory `
                -Path $PublishFolder `
                -Force |
            Out-Null

            Copy-Item `
                -Path (Join-Path $BackupFolder "*") `
                -Destination $PublishFolder `
                -Recurse `
                -Force

            $Changes.Add("Restored the previous deployment from backup.")
        }
        catch {
            $Warnings.Add(
                "Rollback copy failed: " +
                $_.Exception.Message
            )
        }
    }
    elseif ($NewPublishInstalled) {
        $Warnings.Add(
            "No previous publish backup existed, so there was nothing to restore."
        )
    }

    # --------------------------------------------------------
    # Restart the previous deployment
    # --------------------------------------------------------

    try {
        $RollbackConnection = Start-MaximoROWebTask

        $Checks = @(Test-MaximoROWeb)

        $RollbackFailedChecks = $Checks |
        Where-Object {
            $_.Status -ne 200
        }

        if ($RollbackFailedChecks) {
            $Warnings.Add(
                "The rollback process started, but an HTTP health check still failed."
            )
        }
        else {
            $Changes.Add("Rollback deployment is online and healthy.")
        }
    }
    catch {
        $Warnings.Add(
            "Could not restart MaximoROWeb after rollback: " +
            $_.Exception.Message
        )
    }

    Write-DeploymentReport `
        -Result "FAILED — ROLLBACK ATTEMPTED" `
        -FailureMessage $FailureMessage

    Write-Host ""
    Write-Host "Deployment failed." -ForegroundColor Red
    Write-Host $FailureMessage
    Write-Host "Report:"
    Write-Host $Report

    exit 1
}