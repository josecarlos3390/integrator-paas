using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceListPolling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "polling_cursors",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_update_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_update_ts = table.Column<int>(type: "integer", nullable: false),
                    last_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polling_cursors", x => new { x.tenant_id, x.entity_type });
                });

            migrationBuilder.CreateTable(
                name: "price_snapshots",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price_list = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    price_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sap_update_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sap_update_ts = table.Column<int>(type: "integer", nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_snapshots", x => new { x.tenant_id, x.item_code, x.price_list });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "polling_cursors");

            migrationBuilder.DropTable(
                name: "price_snapshots");
        }
    }
}
