$ErrorActionPreference = "Stop"

$PublishFolder = "C:\Projects\MaximoROweb\publish"
$Dll = Join-Path $PublishFolder "MaximoROWeb.dll"

if (-not (Test-Path $Dll)) {
    throw "Published application DLL was not found: $Dll"
}

Set-Location $PublishFolder

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5041"

& dotnet $Dll
