$ErrorActionPreference = "Stop"

$PublishFolder = "C:\Projects\MaximoROWeb\publish-5043"
$Dll = Join-Path $PublishFolder "MaximoROWeb.dll"
$Port = 5043
$UserSecretsId = "11ee85d3-a405-4baf-8690-bf6255182b57"
$UserSecretsFile = Join-Path `
    $env:APPDATA `
    "Microsoft\UserSecrets\$UserSecretsId\secrets.json"

if (-not (Test-Path $UserSecretsFile)) {
    throw "MaximoROWeb production secrets were not found."
}

$UserSecrets =
    Get-Content -LiteralPath $UserSecretsFile -Raw |
    ConvertFrom-Json

$RequiredSecrets = @(
    "ConnectionStrings:RathenaDatabase",
    "EmailVerification:PublicBaseUrl",
    "EmailVerification:Smtp:Enabled",
    "EmailVerification:Smtp:Host",
    "EmailVerification:Smtp:Port",
    "EmailVerification:Smtp:EnableSsl",
    "EmailVerification:Smtp:Username",
    "EmailVerification:Smtp:Password",
    "EmailVerification:Smtp:FromAddress",
    "EmailVerification:Smtp:FromName"
)

foreach ($Key in $RequiredSecrets) {
    $Secret = $UserSecrets.PSObject.Properties[$Key]

    if (-not $Secret -or [string]::IsNullOrWhiteSpace([string]$Secret.Value)) {
        throw "Required MaximoROWeb production secret is missing: $Key"
    }

    $EnvironmentKey = $Key.Replace(":", "__")
    [Environment]::SetEnvironmentVariable(
        $EnvironmentKey,
        [string]$Secret.Value,
        "Process")
}

if (-not (Test-Path $Dll)) {
    throw "Published application DLL was not found: $Dll"
}

Set-Location $PublishFolder

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"

& dotnet $Dll
