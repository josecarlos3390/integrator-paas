# ============================================================================
# Script para actualizar configuracion de tenant en PostgreSQL (Docker)
# ============================================================================
# Uso:
#   .\scripts\update-tenant.ps1 -CrmUrl "https://nuevo-crm.com"
#   .\scripts\update-tenant.ps1 -SapUser "jdiaz" -SapPass "NuevaPass"
#   .\scripts\update-tenant.ps1 -ApiKey "mi-nueva-api-key"
# ============================================================================

param(
    [string]$TenantId = "tenant-001",
    [string]$Name,
    [string]$CrmUrl,
    [string]$SapUrl,
    [string]$SapCompanyDb,
    [string]$SapUser,
    [string]$SapPass,
    [string]$ApiKey
)

$ErrorActionPreference = "Stop"
$containerName = "integration-postgres"

function Invoke-PostgresQuery($sql) {
    $result = $sql | docker exec -i $containerName psql -U integration -d integration_bus -q 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker exec fallo: $result"
    }
    return $result
}

# Verificar contenedor
$running = docker ps --format "{{.Names}}" | Select-String $containerName
if (-not $running) {
    Write-Host "ERROR: El contenedor '$containerName' no esta corriendo." -ForegroundColor Red
    Write-Host "Ejecuta primero: docker-compose up -d postgres" -ForegroundColor Yellow
    exit 1
}

$literalUpdates = @()
if ($Name) { $literalUpdates += "name = '$($Name -replace "'","''")'" }
if ($CrmUrl) { $literalUpdates += "crm_base_url = '$($CrmUrl -replace "'","''")'" }
if ($SapUrl) { $literalUpdates += "sap_service_layer_url = '$($SapUrl -replace "'","''")'" }
if ($SapCompanyDb) { $literalUpdates += "sap_company_db = '$($SapCompanyDb -replace "'","''")'" }
if ($SapUser) { $literalUpdates += "sap_user_name = '$($SapUser -replace "'","''")'" }
if ($SapPass) { $literalUpdates += "sap_password_encrypted = '$($SapPass -replace "'","''")'" }
if ($ApiKey) {
    $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($ApiKey))).ToLower()
    $literalUpdates += "api_key_hash = '$hash'"
}

if ($literalUpdates.Count -eq 0) {
    Write-Host "No se especifico ningun campo para actualizar." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Ejemplos:" -ForegroundColor Cyan
    Write-Host '  .\scripts\update-tenant.ps1 -CrmUrl "https://nuevo-crm.com"' -ForegroundColor White
    Write-Host '  .\scripts\update-tenant.ps1 -SapUser "jdiaz" -SapPass "NuevaPass"' -ForegroundColor White
    Write-Host '  .\scripts\update-tenant.ps1 -ApiKey "mi-nueva-api-key"' -ForegroundColor White
    exit 0
}

$setClause = $literalUpdates -join ", "
$finalSql = "UPDATE tenant_config SET $setClause WHERE tenant_id = '$TenantId';"

Write-Host "Actualizando tenant '$TenantId'..." -ForegroundColor Cyan

try {
    Invoke-PostgresQuery $finalSql | Out-Null
    Write-Host "OK. Tenant actualizado." -ForegroundColor Green

    $verifySql = "SELECT tenant_id, name, crm_base_url, sap_company_db, sap_user_name, is_active FROM tenant_config WHERE tenant_id = '$TenantId';"
    $result = Invoke-PostgresQuery $verifySql
    Write-Host ""
    Write-Host "Valores actuales:" -ForegroundColor Cyan
    Write-Host $result
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
