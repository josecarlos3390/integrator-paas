using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSnakeCaseNamingConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_tenant_feature_flags",
                table: "tenant_feature_flags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_tenant_config",
                table: "tenant_config");

            migrationBuilder.DropPrimaryKey(
                name: "PK_processed_messages",
                table: "processed_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_events",
                table: "outbox_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_integration_logs",
                table: "integration_logs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_integration_alerts",
                table: "integration_alerts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records");

            migrationBuilder.DropPrimaryKey(
                name: "PK_dead_letter_events",
                table: "dead_letter_events");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "tenant_feature_flags",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "tenant_feature_flags",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "FeatureKey",
                table: "tenant_feature_flags",
                newName: "feature_key");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "tenant_feature_flags",
                newName: "tenant_id");

            migrationBuilder.RenameIndex(
                name: "IX_tenant_feature_flags_TenantId",
                table: "tenant_feature_flags",
                newName: "ix_tenant_feature_flags_tenant_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "tenant_config",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "SapUserName",
                table: "tenant_config",
                newName: "sap_user_name");

            migrationBuilder.RenameColumn(
                name: "SapServiceLayerUrl",
                table: "tenant_config",
                newName: "sap_service_layer_url");

            migrationBuilder.RenameColumn(
                name: "SapPasswordEncrypted",
                table: "tenant_config",
                newName: "sap_password_encrypted");

            migrationBuilder.RenameColumn(
                name: "SapCompanyDb",
                table: "tenant_config",
                newName: "sap_company_db");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "tenant_config",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CrmBaseUrl",
                table: "tenant_config",
                newName: "crm_base_url");

            migrationBuilder.RenameColumn(
                name: "CrmApiKeyEncrypted",
                table: "tenant_config",
                newName: "crm_api_key_encrypted");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "tenant_config",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ApiKeyHash",
                table: "tenant_config",
                newName: "api_key_hash");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "tenant_config",
                newName: "tenant_id");

            migrationBuilder.RenameIndex(
                name: "IX_tenant_config_IsActive",
                table: "tenant_config",
                newName: "ix_tenant_config_is_active");

            migrationBuilder.RenameColumn(
                name: "Consumer",
                table: "processed_messages",
                newName: "consumer");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "processed_messages",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ProcessedAt",
                table: "processed_messages",
                newName: "processed_at");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "processed_messages",
                newName: "message_id");

            migrationBuilder.RenameIndex(
                name: "IX_processed_messages_MessageId_Consumer",
                table: "processed_messages",
                newName: "ix_processed_messages_message_id_consumer");

            migrationBuilder.RenameColumn(
                name: "Payload",
                table: "outbox_events",
                newName: "payload");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "outbox_events",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "outbox_events",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ProcessedAt",
                table: "outbox_events",
                newName: "processed_at");

            migrationBuilder.RenameColumn(
                name: "OccurredAt",
                table: "outbox_events",
                newName: "occurred_at");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "outbox_events",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "outbox_events",
                newName: "error_message");

            migrationBuilder.RenameColumn(
                name: "AttemptCount",
                table: "outbox_events",
                newName: "attempt_count");

            migrationBuilder.RenameColumn(
                name: "AggregateId",
                table: "outbox_events",
                newName: "aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_events_TenantId_AggregateId",
                table: "outbox_events",
                newName: "ix_outbox_events_tenant_id_aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_events_ProcessedAt_AttemptCount",
                table: "outbox_events",
                newName: "ix_outbox_events_processed_at_attempt_count");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "integration_logs",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Direction",
                table: "integration_logs",
                newName: "direction");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "integration_logs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "integration_logs",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SapDocEntry",
                table: "integration_logs",
                newName: "sap_doc_entry");

            migrationBuilder.RenameColumn(
                name: "ExternalId",
                table: "integration_logs",
                newName: "external_id");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "integration_logs",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "integration_logs",
                newName: "error_message");

            migrationBuilder.RenameColumn(
                name: "DurationMs",
                table: "integration_logs",
                newName: "duration_ms");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "integration_logs",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "integration_logs",
                newName: "correlation_id");

            migrationBuilder.RenameIndex(
                name: "IX_integration_logs_TenantId_CorrelationId",
                table: "integration_logs",
                newName: "ix_integration_logs_tenant_id_correlation_id");

            migrationBuilder.RenameIndex(
                name: "IX_integration_logs_CreatedAt",
                table: "integration_logs",
                newName: "ix_integration_logs_created_at");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "integration_alerts",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Severity",
                table: "integration_alerts",
                newName: "severity");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "integration_alerts",
                newName: "message");

            migrationBuilder.RenameColumn(
                name: "Details",
                table: "integration_alerts",
                newName: "details");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "integration_alerts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "integration_alerts",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "IsAcknowledged",
                table: "integration_alerts",
                newName: "is_acknowledged");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "integration_alerts",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AlertType",
                table: "integration_alerts",
                newName: "alert_type");

            migrationBuilder.RenameColumn(
                name: "AcknowledgedBy",
                table: "integration_alerts",
                newName: "acknowledged_by");

            migrationBuilder.RenameColumn(
                name: "AcknowledgedAt",
                table: "integration_alerts",
                newName: "acknowledged_at");

            migrationBuilder.RenameIndex(
                name: "IX_integration_alerts_TenantId_IsAcknowledged_CreatedAt",
                table: "integration_alerts",
                newName: "ix_integration_alerts_tenant_id_is_acknowledged_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_integration_alerts_AlertType",
                table: "integration_alerts",
                newName: "ix_integration_alerts_alert_type");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "idempotency_records",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "idempotency_records",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "idempotency_records",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ProcessedAt",
                table: "idempotency_records",
                newName: "processed_at");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "idempotency_records",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "idempotency_records",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "AggregateId",
                table: "idempotency_records",
                newName: "aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_TenantId_EventType_AggregateId",
                table: "idempotency_records",
                newName: "ix_idempotency_records_tenant_id_event_type_aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_ExpiresAt",
                table: "idempotency_records",
                newName: "ix_idempotency_records_expires_at");

            migrationBuilder.RenameColumn(
                name: "Source",
                table: "dead_letter_events",
                newName: "source");

            migrationBuilder.RenameColumn(
                name: "Payload",
                table: "dead_letter_events",
                newName: "payload");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "dead_letter_events",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "dead_letter_events",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "OccurredAt",
                table: "dead_letter_events",
                newName: "occurred_at");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "dead_letter_events",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "dead_letter_events",
                newName: "error_message");

            migrationBuilder.RenameColumn(
                name: "DeadLetteredAt",
                table: "dead_letter_events",
                newName: "dead_lettered_at");

            migrationBuilder.RenameColumn(
                name: "AttemptCount",
                table: "dead_letter_events",
                newName: "attempt_count");

            migrationBuilder.RenameColumn(
                name: "AggregateId",
                table: "dead_letter_events",
                newName: "aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_dead_letter_events_TenantId_DeadLetteredAt",
                table: "dead_letter_events",
                newName: "ix_dead_letter_events_tenant_id_dead_lettered_at");

            migrationBuilder.AddPrimaryKey(
                name: "pk_tenant_feature_flags",
                table: "tenant_feature_flags",
                columns: new[] { "tenant_id", "feature_key" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_tenant_config",
                table: "tenant_config",
                column: "tenant_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_processed_messages",
                table: "processed_messages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_events",
                table: "outbox_events",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_integration_logs",
                table: "integration_logs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_integration_alerts",
                table: "integration_alerts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_records",
                table: "idempotency_records",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_dead_letter_events",
                table: "dead_letter_events",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_tenant_feature_flags",
                table: "tenant_feature_flags");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tenant_config",
                table: "tenant_config");

            migrationBuilder.DropPrimaryKey(
                name: "pk_processed_messages",
                table: "processed_messages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_events",
                table: "outbox_events");

            migrationBuilder.DropPrimaryKey(
                name: "pk_integration_logs",
                table: "integration_logs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_integration_alerts",
                table: "integration_alerts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_records",
                table: "idempotency_records");

            migrationBuilder.DropPrimaryKey(
                name: "pk_dead_letter_events",
                table: "dead_letter_events");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "tenant_feature_flags",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "tenant_feature_flags",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "feature_key",
                table: "tenant_feature_flags",
                newName: "FeatureKey");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "tenant_feature_flags",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_feature_flags_tenant_id",
                table: "tenant_feature_flags",
                newName: "IX_tenant_feature_flags_TenantId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "tenant_config",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "sap_user_name",
                table: "tenant_config",
                newName: "SapUserName");

            migrationBuilder.RenameColumn(
                name: "sap_service_layer_url",
                table: "tenant_config",
                newName: "SapServiceLayerUrl");

            migrationBuilder.RenameColumn(
                name: "sap_password_encrypted",
                table: "tenant_config",
                newName: "SapPasswordEncrypted");

            migrationBuilder.RenameColumn(
                name: "sap_company_db",
                table: "tenant_config",
                newName: "SapCompanyDb");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "tenant_config",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "crm_base_url",
                table: "tenant_config",
                newName: "CrmBaseUrl");

            migrationBuilder.RenameColumn(
                name: "crm_api_key_encrypted",
                table: "tenant_config",
                newName: "CrmApiKeyEncrypted");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "tenant_config",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "api_key_hash",
                table: "tenant_config",
                newName: "ApiKeyHash");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "tenant_config",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_config_is_active",
                table: "tenant_config",
                newName: "IX_tenant_config_IsActive");

            migrationBuilder.RenameColumn(
                name: "consumer",
                table: "processed_messages",
                newName: "Consumer");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "processed_messages",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "processed_at",
                table: "processed_messages",
                newName: "ProcessedAt");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "processed_messages",
                newName: "MessageId");

            migrationBuilder.RenameIndex(
                name: "ix_processed_messages_message_id_consumer",
                table: "processed_messages",
                newName: "IX_processed_messages_MessageId_Consumer");

            migrationBuilder.RenameColumn(
                name: "payload",
                table: "outbox_events",
                newName: "Payload");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "outbox_events",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "outbox_events",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "processed_at",
                table: "outbox_events",
                newName: "ProcessedAt");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "outbox_events",
                newName: "OccurredAt");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "outbox_events",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "outbox_events",
                newName: "ErrorMessage");

            migrationBuilder.RenameColumn(
                name: "attempt_count",
                table: "outbox_events",
                newName: "AttemptCount");

            migrationBuilder.RenameColumn(
                name: "aggregate_id",
                table: "outbox_events",
                newName: "AggregateId");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_events_tenant_id_aggregate_id",
                table: "outbox_events",
                newName: "IX_outbox_events_TenantId_AggregateId");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_events_processed_at_attempt_count",
                table: "outbox_events",
                newName: "IX_outbox_events_ProcessedAt_AttemptCount");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "integration_logs",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "direction",
                table: "integration_logs",
                newName: "Direction");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "integration_logs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "integration_logs",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "sap_doc_entry",
                table: "integration_logs",
                newName: "SapDocEntry");

            migrationBuilder.RenameColumn(
                name: "external_id",
                table: "integration_logs",
                newName: "ExternalId");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "integration_logs",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "integration_logs",
                newName: "ErrorMessage");

            migrationBuilder.RenameColumn(
                name: "duration_ms",
                table: "integration_logs",
                newName: "DurationMs");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "integration_logs",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "integration_logs",
                newName: "CorrelationId");

            migrationBuilder.RenameIndex(
                name: "ix_integration_logs_tenant_id_correlation_id",
                table: "integration_logs",
                newName: "IX_integration_logs_TenantId_CorrelationId");

            migrationBuilder.RenameIndex(
                name: "ix_integration_logs_created_at",
                table: "integration_logs",
                newName: "IX_integration_logs_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "integration_alerts",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "severity",
                table: "integration_alerts",
                newName: "Severity");

            migrationBuilder.RenameColumn(
                name: "message",
                table: "integration_alerts",
                newName: "Message");

            migrationBuilder.RenameColumn(
                name: "details",
                table: "integration_alerts",
                newName: "Details");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "integration_alerts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "integration_alerts",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "is_acknowledged",
                table: "integration_alerts",
                newName: "IsAcknowledged");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "integration_alerts",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "alert_type",
                table: "integration_alerts",
                newName: "AlertType");

            migrationBuilder.RenameColumn(
                name: "acknowledged_by",
                table: "integration_alerts",
                newName: "AcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "acknowledged_at",
                table: "integration_alerts",
                newName: "AcknowledgedAt");

            migrationBuilder.RenameIndex(
                name: "ix_integration_alerts_tenant_id_is_acknowledged_created_at",
                table: "integration_alerts",
                newName: "IX_integration_alerts_TenantId_IsAcknowledged_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_integration_alerts_alert_type",
                table: "integration_alerts",
                newName: "IX_integration_alerts_AlertType");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "idempotency_records",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "idempotency_records",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "idempotency_records",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "processed_at",
                table: "idempotency_records",
                newName: "ProcessedAt");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "idempotency_records",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "idempotency_records",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "aggregate_id",
                table: "idempotency_records",
                newName: "AggregateId");

            migrationBuilder.RenameIndex(
                name: "ix_idempotency_records_tenant_id_event_type_aggregate_id",
                table: "idempotency_records",
                newName: "IX_idempotency_records_TenantId_EventType_AggregateId");

            migrationBuilder.RenameIndex(
                name: "ix_idempotency_records_expires_at",
                table: "idempotency_records",
                newName: "IX_idempotency_records_ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "source",
                table: "dead_letter_events",
                newName: "Source");

            migrationBuilder.RenameColumn(
                name: "payload",
                table: "dead_letter_events",
                newName: "Payload");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "dead_letter_events",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "dead_letter_events",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "dead_letter_events",
                newName: "OccurredAt");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "dead_letter_events",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "dead_letter_events",
                newName: "ErrorMessage");

            migrationBuilder.RenameColumn(
                name: "dead_lettered_at",
                table: "dead_letter_events",
                newName: "DeadLetteredAt");

            migrationBuilder.RenameColumn(
                name: "attempt_count",
                table: "dead_letter_events",
                newName: "AttemptCount");

            migrationBuilder.RenameColumn(
                name: "aggregate_id",
                table: "dead_letter_events",
                newName: "AggregateId");

            migrationBuilder.RenameIndex(
                name: "ix_dead_letter_events_tenant_id_dead_lettered_at",
                table: "dead_letter_events",
                newName: "IX_dead_letter_events_TenantId_DeadLetteredAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_tenant_feature_flags",
                table: "tenant_feature_flags",
                columns: new[] { "TenantId", "FeatureKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_tenant_config",
                table: "tenant_config",
                column: "TenantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_processed_messages",
                table: "processed_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_events",
                table: "outbox_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_integration_logs",
                table: "integration_logs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_integration_alerts",
                table: "integration_alerts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_dead_letter_events",
                table: "dead_letter_events",
                column: "Id");
        }
    }
}
