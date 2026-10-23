#Requires -RunAsAdministrator
<#
Actualiza y reinicia SOLO el API (PaasVTEX), sin tocar Integration.Worker.
Ejecutar en una consola elevada:

    cd C:\ProyectosPython\integrator-paas
    .\scripts\update-api.ps1
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== Deteniendo Integration.Api ==" -ForegroundColor Cyan
$service = Get-Service -Name Integration.Api -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne "Stopped") {
    Stop-Service -Name Integration.Api -Force
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

# Si quedo un proceso colgado con el binario publicado, cerrarlo.
Get-CimInstance Win32_Process -Filter "Name='Integration.Api.exe'" |
    Where-Object { $_.ExecutablePath -like "*\.publish\Api\*" } |
    ForEach-Object {
        Write-Host "Cerrando proceso colgado PID $($_.ProcessId)" -ForegroundColor Yellow
        Stop-Process -Id $_.ProcessId -Force
    }

Write-Host "== Publicando API en Release ==" -ForegroundColor Cyan
dotnet publish "src\Integration.Api\Integration.Api.csproj" `
    -c Release `
    -o ".publish\Api" `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish fallo con codigo $LASTEXITCODE"
}

# Mantener la configuracion del servidor (Vtex, RETAIL_QA5, Telegram, etc.).
$production = "src\Integration.Api\appsettings.Production.json"
if (Test-Path $production) {
    Copy-Item $production ".publish\Api\appsettings.Production.json" -Force
}

Write-Host "== Iniciando Integration.Api ==" -ForegroundColor Cyan
Start-Service -Name Integration.Api
(Get-Service -Name Integration.Api).WaitForStatus(
    "Running", [TimeSpan]::FromSeconds(30)
)
Start-Sleep -Seconds 2

Write-Host "== Verificacion ==" -ForegroundColor Cyan
Get-Service -Name Integration.Api |
    Select-Object Name, Status, StartType |
    Format-Table -AutoSize

Get-Item ".publish\Api\Integration.Api.dll" |
    Select-Object LastWriteTime, Length |
    Format-List

Invoke-WebRequest "http://127.0.0.1:5050/health/live" -UseBasicParsing |
    Select-Object StatusCode, Content

Invoke-WebRequest "http://127.0.0.1:5050/api/vtex/orders?page=1&perPage=1" `
    -Headers @{ "X-Api-Key" = "chatbot-dev-key" } `
    -UseBasicParsing |
    Select-Object StatusCode
