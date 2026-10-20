param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$HanaConnectionString,

    [Parameter(Mandatory = $true)]
    [string]$SapServiceLayerUrl,

    [Parameter(Mandatory = $true)]
    [string]$SapCompanyDb
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\src\Integration.Worker\Integration.Worker.csproj"

$env:Tenants__DefaultTenantId = $TenantId
$env:Hana__ConnectionString = $HanaConnectionString
$env:Sap__ServiceLayerUrl = $SapServiceLayerUrl
$env:Sap__CompanyDB = $SapCompanyDb

Write-Host "Starting worker for tenant $TenantId ($SapCompanyDb)"
dotnet run --project $project
