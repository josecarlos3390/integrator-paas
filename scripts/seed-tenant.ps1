# ============================================================================
# Seed: Inserta el tenant por defecto (tenant-001) en PostgreSQL (Docker)
# ============================================================================

$ErrorActionPreference = "Stop"
$containerName = "integration-postgres"

# Verificar contenedor
$running = docker ps --format "{{.Names}}" | Select-String $containerName
if (-not $running) {
    Write-Host "ERROR: El contenedor '$containerName' no esta corriendo." -ForegroundColor Red
    Write-Host "Ejecuta primero: docker-compose up -d postgres" -ForegroundColor Yellow
    exit 1
}

Write-Host "Insertando tenant 'tenant-001' en PostgreSQL..." -ForegroundColor Cyan

$seedSql = @"
INSERT INTO tenant_config (
    tenant_id, name, api_key_hash,
    sap_service_layer_url, sap_company_db, sap_user_name, sap_password_encrypted,
    crm_base_url, crm_api_key_encrypted, is_active, created_at
)
VALUES (
    'tenant-001',
    'Viaggio QA',
    '2C26B46B68FFC68FF99B453C1D30413413422D706483BFA0F98A5E886266E7AE',
    'https://hanaroda.gruporoda.com:50000',
    'VIAGGIO_QA',
    'jdiaz',
    'HoN3390',
    'http://localhost:5000',
    'mock-api-key',
    true,
    NOW()
)
ON CONFLICT (tenant_id) DO UPDATE SET
    name = EXCLUDED.name,
    api_key_hash = EXCLUDED.api_key_hash,
    sap_service_layer_url = EXCLUDED.sap_service_layer_url,
    sap_company_db = EXCLUDED.sap_company_db,
    sap_user_name = EXCLUDED.sap_user_name,
    sap_password_encrypted = EXCLUDED.sap_password_encrypted,
    crm_base_url = EXCLUDED.crm_base_url,
    crm_api_key_encrypted = EXCLUDED.crm_api_key_encrypted,
    is_active = true;

SELECT tenant_id, name, crm_base_url, sap_company_db, sap_user_name, is_active
FROM tenant_config
WHERE tenant_id = 'tenant-001';
"@

try {
    $result = $seedSql | docker exec -i $containerName psql -U integration -d integration_bus -q 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker exec fallo con codigo $LASTEXITCODE"
    }
    Write-Host ""
    Write-Host "Tenant 'tenant-001' insertado/actualizado correctamente." -ForegroundColor Green
    Write-Host ""
    Write-Host "Valores actuales:" -ForegroundColor Cyan
    Write-Host $result
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
