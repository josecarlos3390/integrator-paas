# install-windows-services.ps1
# Registers Integration.Api and Integration.Worker as Windows Services with
# automatic startup and restart-on-failure recovery.
#
# Usage (elevated PowerShell — right click, "Run as administrator"):
#   powershell -ExecutionPolicy Bypass -File scripts\install-windows-services.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\install-windows-services.ps1 -Uninstall
#
# Requires the projects to be published first:
#   dotnet publish src/Integration.Api -c Release -o .publish/Api
#   dotnet publish src/Integration.Worker -c Release -o .publish/Worker

param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$services = @(
    @{
        Name        = 'Integration.Api'
        DisplayName = 'Integration Bus API'
        Description = 'Integration Bus REST API (SAP <-> CRM)'
        BinPath     = Join-Path $root '.publish\Api\Integration.Api.exe'
    },
    @{
        Name        = 'Integration.Worker'
        DisplayName = 'Integration Bus Worker'
        Description = 'Integration Bus background workers (HANA outbox dispatcher, ingestion, DLQ retry)'
        BinPath     = Join-Path $root '.publish\Worker\Integration.Worker.exe'
    }
)

foreach ($svc in $services) {
    if ($Uninstall) {
        if (Get-Service -Name $svc.Name -ErrorAction SilentlyContinue) {
            Write-Host "Stopping and removing $($svc.Name)..."
            Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue
            sc.exe delete $svc.Name | Out-Null
            Write-Host "$($svc.Name) removed."
        } else {
            Write-Host "$($svc.Name) is not installed."
        }
        continue
    }

    if (-not (Test-Path $svc.BinPath)) {
        throw "Missing binary: $($svc.BinPath). Run dotnet publish first (see script header)."
    }

    if (Get-Service -Name $svc.Name -ErrorAction SilentlyContinue) {
        Write-Host "$($svc.Name) already exists, skipping creation."
    } else {
        Write-Host "Creating service $($svc.Name)..."
        sc.exe create $svc.Name binPath= "`"$($svc.BinPath)`"" start= auto DisplayName= "$($svc.DisplayName)" | Out-Null
        sc.exe description $svc.Name "$($svc.Description)" | Out-Null
        # Restart on failure: 3 attempts, 60s apart, failure counter resets daily
        sc.exe failure $svc.Name reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    }

    Write-Host "Starting $($svc.Name)..."
    Start-Service -Name $svc.Name
}

if (-not $Uninstall) {
    Write-Host ""
    Write-Host "Service status:"
    Get-Service -Name $services.Name | Format-Table Name, Status, StartType -AutoSize
}
