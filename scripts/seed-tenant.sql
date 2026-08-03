-- ============================================================================
-- Seed: Tenant por defecto (tenant-001)
-- ============================================================================

INSERT INTO tenant_config (
    tenant_id, name, api_key_hash,
    sap_service_layer_url, sap_company_db, sap_user_name, sap_password_encrypted,
    crm_base_url, crm_api_key_encrypted, crm_connector_type, is_active, created_at
)
VALUES (
    'tenant-001',
    'Viaggio QA',
    '2C26B46B68FFC68FF99B453C1D30413413422D706483BFA0F98A5E886266E7AE', -- SHA-256 de 'mock-api-key'
    'https://hanaroda.gruporoda.com:50000',
    'VIAGGIO_QA',
    'jdiaz',
    'HoN3390',
    'http://localhost:5000',
    'mock-api-key',
    'Mock',
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
    crm_connector_type = EXCLUDED.crm_connector_type,
    is_active = true;

-- Verificar
SELECT * FROM tenant_config;
