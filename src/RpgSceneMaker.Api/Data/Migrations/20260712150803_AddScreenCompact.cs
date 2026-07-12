using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgSceneMaker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenCompact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Compact",
                table: "Screens",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Compact",
                table: "Screens");
        }
    }
}
