using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBankSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: trimmed to vendor_bank_snapshots only. The other tables/indexes that the
            // scaffold included (integration_metric_counters, integration_requests, tenant_quotas,
            // extra integration_logs indexes) already exist in production — they were provisioned
            // via the scripts/*.sql files, not via EF migrations.
            migrationBuilder.CreateTable(
                name: "vendor_bank_snapshots",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    card_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    card_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    bank_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    branch = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    account_no = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    iban = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_bank_snapshots", x => new { x.tenant_id, x.card_code });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_bank_snapshots");
        }
    }
}
