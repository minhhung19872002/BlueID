using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueID.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleUseTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "used_tokens",
                schema: "auth",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_used_tokens", x => new { x.kind, x.value });
                });

            migrationBuilder.CreateIndex(
                name: "ix_used_tokens_expires_at",
                schema: "auth",
                table: "used_tokens",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "used_tokens",
                schema: "auth");
        }
    }
}
