using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertType = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Details = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_alerts_AlertType",
                table: "integration_alerts",
                column: "AlertType");

            migrationBuilder.CreateIndex(
                name: "IX_integration_alerts_TenantId_IsAcknowledged_CreatedAt",
                table: "integration_alerts",
                columns: new[] { "TenantId", "IsAcknowledged", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_alerts");
        }
    }
}
