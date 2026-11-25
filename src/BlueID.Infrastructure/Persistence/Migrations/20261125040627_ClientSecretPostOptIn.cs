using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueID.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSecretPostOptIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_client_secret_post",
                schema: "apps",
                table: "applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_client_secret_post",
                schema: "apps",
                table: "applications");
        }
    }
}
