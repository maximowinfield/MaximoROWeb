$ErrorActionPreference = "Stop"

$PublishFolder = "C:\Projects\MaximoROWeb\publish-5043"
$Dll = Join-Path $PublishFolder "MaximoROWeb.dll"
$Port = 5043

if (-not (Test-Path $Dll)) {
    throw "Published application DLL was not found: $Dll"
}

Set-Location $PublishFolder

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"

& dotnet $Dll
