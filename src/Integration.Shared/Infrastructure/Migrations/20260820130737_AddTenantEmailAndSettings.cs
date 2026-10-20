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

            migrationBuilder.CreateTable(
                name: "integration_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_system = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_system = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    callback_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    result_payload = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    leased_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_requests", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "tenant_settings",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    setting_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    setting_value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_settings", x => new { x.tenant_id, x.setting_key });
                });

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
