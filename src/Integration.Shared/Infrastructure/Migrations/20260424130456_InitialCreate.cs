using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letter_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadLetteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SapDocEntry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Consumer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_config",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SapServiceLayerUrl = table.Column<string>(type: "text", nullable: false),
                    SapCompanyDb = table.Column<string>(type: "text", nullable: false),
                    SapUserName = table.Column<string>(type: "text", nullable: false),
                    SapPasswordEncrypted = table.Column<string>(type: "text", nullable: false),
                    CrmBaseUrl = table.Column<string>(type: "text", nullable: false),
                    CrmApiKeyEncrypted = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_config", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_events_TenantId_DeadLetteredAt",
                table: "dead_letter_events",
                columns: new[] { "TenantId", "DeadLetteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_logs_CreatedAt",
                table: "integration_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_integration_logs_TenantId_CorrelationId",
                table: "integration_logs",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_ProcessedAt_AttemptCount",
                table: "outbox_events",
                columns: new[] { "ProcessedAt", "AttemptCount" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId_AggregateId",
                table: "outbox_events",
                columns: new[] { "TenantId", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_processed_messages_MessageId_Consumer",
                table: "processed_messages",
                columns: new[] { "MessageId", "Consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_config_IsActive",
                table: "tenant_config",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_events");

            migrationBuilder.DropTable(
                name: "integration_logs");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropTable(
                name: "tenant_config");
        }
    }
}
