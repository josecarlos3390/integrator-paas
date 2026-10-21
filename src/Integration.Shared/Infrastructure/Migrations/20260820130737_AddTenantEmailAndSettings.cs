using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_enabled",
                table: "tenant_config",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "email_from_address",
                table: "tenant_config",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "smtp_host",
                table: "tenant_config",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "smtp_password_encrypted",
                table: "tenant_config",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "smtp_port",
                table: "tenant_config",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "smtp_user_name",
                table: "tenant_config",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // These tables/indexes exist in the model but were never created by an
            // EF migration (they pre-exist in the deployed database, created outside
            // the migration history). IF NOT EXISTS keeps the migration applicable
            // both on existing databases and on fresh ones.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS integration_metric_counters (
                    metric_key character varying(128) NOT NULL,
                    metric_value bigint NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_integration_metric_counters PRIMARY KEY (metric_key)
                );
                """);

            // integration_requests also pre-exists in the deployed database.
            // Use IF NOT EXISTS so the migration is applicable both on the
            // existing database and on a fresh one.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS integration_requests (
                    id uuid NOT NULL,
                    tenant_id character varying(64) NOT NULL,
                    correlation_id character varying(64) NOT NULL,
                    source_system character varying(64) NOT NULL,
                    target_system character varying(64) NOT NULL,
                    entity_type character varying(64) NOT NULL,
                    operation character varying(32) NOT NULL,
                    external_id character varying(128) NOT NULL,
                    payload text NOT NULL,
                    callback_url text NULL,
                    status character varying(32) NOT NULL,
                    attempt_count integer NOT NULL,
                    error_message text NULL,
                    result_payload text NULL,
                    priority integer NOT NULL,
                    received_at timestamp with time zone NOT NULL,
                    processed_at timestamp with time zone NULL,
                    next_retry_at timestamp with time zone NULL,
                    leased_until timestamp with time zone NULL,
                    CONSTRAINT pk_integration_requests PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS tenant_quotas (
                    tenant_id character varying(64) NOT NULL,
                    max_events_per_hour integer NOT NULL,
                    max_api_calls_per_minute integer NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_tenant_quotas PRIMARY KEY (tenant_id)
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS tenant_settings (
                    tenant_id character varying(64) NOT NULL,
                    setting_key character varying(128) NOT NULL,
                    setting_value text NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_tenant_settings PRIMARY KEY (tenant_id, setting_key)
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_integration_logs_created_at_status
                ON integration_logs (created_at, status);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_integration_logs_created_at_tenant_id",
                table: "integration_logs",
                columns: new[] { "created_at", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_requests_received_at",
                table: "integration_requests",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_integration_requests_status_leased_until",
                table: "integration_requests",
                columns: new[] { "status", "leased_until" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_requests_tenant_id_external_id_entity_type",
                table: "integration_requests",
                columns: new[] { "tenant_id", "external_id", "entity_type" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_requests_tenant_id_status_next_retry_at",
                table: "integration_requests",
                columns: new[] { "tenant_id", "status", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_settings_tenant_id",
                table: "tenant_settings",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop what Up created (integration_metric_counters and
            // tenant_quotas pre-existed outside the migration history).
            migrationBuilder.DropTable(
                name: "integration_requests");

            migrationBuilder.DropTable(
                name: "tenant_settings");

            migrationBuilder.DropIndex(
                name: "ix_integration_logs_created_at_tenant_id",
                table: "integration_logs");

            migrationBuilder.DropColumn(
                name: "email_enabled",
                table: "tenant_config");

            migrationBuilder.DropColumn(
                name: "email_from_address",
                table: "tenant_config");

            migrationBuilder.DropColumn(
                name: "smtp_host",
                table: "tenant_config");

            migrationBuilder.DropColumn(
                name: "smtp_password_encrypted",
                table: "tenant_config");

            migrationBuilder.DropColumn(
                name: "smtp_port",
                table: "tenant_config");

            migrationBuilder.DropColumn(
                name: "smtp_user_name",
                table: "tenant_config");
        }
    }
}
