using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_ExpiresAt",
                table: "idempotency_records",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_EventType_AggregateId",
                table: "idempotency_records",
                columns: new[] { "TenantId", "EventType", "AggregateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
