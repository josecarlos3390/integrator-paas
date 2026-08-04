# redeploy.ps1
# Redeploys Integration.Api and Integration.Worker after code/config changes:
# stops the services, publishes Release binaries to .publish\, makes sure the
# firewall allows the API port, and starts the services again.
#
# Usage (elevated PowerShell — right click, "Run as administrator"):
#   powershell -ExecutionPolicy Bypass -File scripts\redeploy.ps1

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$apiPort = 5050

Write-Host "Stopping services..."
Stop-Service Integration.Api, Integration.Worker -Force -ErrorAction SilentlyContinue

Write-Host "Publishing Integration.Api..."
& $dotnet publish (Join-Path $root 'src\Integration.Api') -c Release -o (Join-Path $root '.publish\Api') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Integration.Api" }

Write-Host "Publishing Integration.Worker..."
& $dotnet publish (Join-Path $root 'src\Integration.Worker') -c Release -o (Join-Path $root '.publish\Worker') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Integration.Worker" }

# Firewall: allow inbound API port (idempotent)
$ruleName = "Integration API $apiPort"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    Write-Host "Creating firewall rule '$ruleName'..."
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $apiPort -Action Allow | Out-Null
}

Write-Host "Starting services..."
Start-Service Integration.Api, Integration.Worker

Write-Host ""
Get-Service Integration.Api, Integration.Worker | Format-Table Name, Status, StartType -AutoSize
Write-Host "Redeploy complete. Panel: http://<servidor>:$apiPort/index.html"
