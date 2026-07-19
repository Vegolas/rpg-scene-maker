using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FreesoundImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Sounds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "License",
                table: "Sounds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseUrl",
                table: "Sounds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Sounds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FreesoundConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreesoundConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FreesoundConfigs");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "Sounds");

            migrationBuilder.DropColumn(
                name: "License",
                table: "Sounds");

            migrationBuilder.DropColumn(
                name: "LicenseUrl",
                table: "Sounds");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Sounds");
        }
    }
}
