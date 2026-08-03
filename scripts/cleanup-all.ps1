# ============================================================================
# Limpieza completa del integrador para circuitos de prueba limpios
# ============================================================================
# Este script limpia PostgreSQL via docker exec (el contenedor debe estar corriendo)
# HANA debe limpiarse manualmente via HANA Studio / DBeaver ejecutando cleanup-hana.sql
# ============================================================================

$ErrorActionPreference = "Stop"

# ============================================================================
# 1. PostgreSQL (via docker exec)
# ============================================================================
Write-Host "Limpiando PostgreSQL via docker exec..." -ForegroundColor Cyan

$containerName = "integration-postgres"
$truncateSql = @"
TRUNCATE TABLE outbox_events RESTART IDENTITY CASCADE;
TRUNCATE TABLE integration_logs RESTART IDENTITY CASCADE;
TRUNCATE TABLE dead_letter_events RESTART IDENTITY CASCADE;
TRUNCATE TABLE idempotency_records RESTART IDENTITY CASCADE;
TRUNCATE TABLE integration_alerts RESTART IDENTITY CASCADE;
TRUNCATE TABLE processed_messages RESTART IDENTITY CASCADE;
TRUNCATE TABLE tenant_feature_flags RESTART IDENTITY CASCADE;

SELECT 'outbox_events' AS tabla, COUNT(*) AS registros FROM outbox_events
UNION ALL SELECT 'integration_logs', COUNT(*) FROM integration_logs
UNION ALL SELECT 'dead_letter_events', COUNT(*) FROM dead_letter_events
UNION ALL SELECT 'idempotency_records', COUNT(*) FROM idempotency_records
UNION ALL SELECT 'integration_alerts', COUNT(*) FROM integration_alerts
UNION ALL SELECT 'processed_messages', COUNT(*) FROM processed_messages
UNION ALL SELECT 'tenant_feature_flags', COUNT(*) FROM tenant_feature_flags;
"@

try {
    $result = $truncateSql | docker exec -i $containerName psql -U integration -d integration_bus -q 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker exec fallo con codigo $LASTEXITCODE"
    }
    Write-Host ""
    Write-Host "PostgreSQL limpio correctamente." -ForegroundColor Green
    Write-Host "Resultado:"
    Write-Host $result
} catch {
    Write-Host ""
    Write-Host "ERROR al limpiar PostgreSQL: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Asegurate de que el contenedor '$containerName' este corriendo:" -ForegroundColor Yellow
    Write-Host "  docker-compose up -d postgres" -ForegroundColor White
    Write-Host ""
    Write-Host "Alternativa manual:" -ForegroundColor Yellow
    Write-Host "  1. Conecta DBeaver / pgAdmin / DataGrip a localhost:5434" -ForegroundColor White
    Write-Host "  2. Ejecuta el contenido de scripts/cleanup-postgres.sql" -ForegroundColor White
    exit 1
}

# ============================================================================
# 2. Instrucciones para HANA
# ============================================================================
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "IMPORTANTE: HANA debe limpiarse MANUALMENTE" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Ejecuta en HANA Studio / DBeaver:" -ForegroundColor White
Write-Host "  TRUNCATE TABLE INTEGRATION_BUS.OUTBOX_EVENTS;" -ForegroundColor White
Write-Host ""
Write-Host "Verifica que quedo vacia:" -ForegroundColor White
Write-Host "  SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS;" -ForegroundColor White
Write-Host ""
Write-Host "Una vez limpio HANA, recrea el SP v2:" -ForegroundColor White
Write-Host "  scripts/hana-post-transaction-notice-v2.sql" -ForegroundColor White
Write-Host ""
Write-Host "Luego podes levantar API + Worker y hacer circuitos de prueba." -ForegroundColor Green
