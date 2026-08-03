using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.Shared.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmConnectorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "crm_connector_type",
                table: "tenant_config",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "crm_connector_type",
                table: "tenant_config");
        }
    }
}
