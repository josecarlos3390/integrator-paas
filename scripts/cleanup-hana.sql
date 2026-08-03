-- ============================================================================
-- Limpieza completa de HANA OUTBOX_EVENTS para circuitos de prueba limpios
-- ============================================================================

-- Truncar la tabla de eventos (más rápido que DELETE para volver a empezar)
TRUNCATE TABLE INTEGRATION_BUS.OUTBOX_EVENTS;

-- Verificar que quedó vacía
SELECT 'Total eventos despues de truncate' AS descripcion, COUNT(*) AS count FROM INTEGRATION_BUS.OUTBOX_EVENTS;
