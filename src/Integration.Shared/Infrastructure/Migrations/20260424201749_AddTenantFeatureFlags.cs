using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_feature_flags",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_feature_flags", x => new { x.TenantId, x.FeatureKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_feature_flags_TenantId",
                table: "tenant_feature_flags",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_feature_flags");
        }
    }
}
