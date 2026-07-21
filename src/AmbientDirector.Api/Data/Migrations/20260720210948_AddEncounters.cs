using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEncounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Spotlight",
                table: "Enemies");

            migrationBuilder.AddColumn<string>(
                name: "Portrait",
                table: "Enemies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Encounters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    HeroIds = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundImage = table.Column<string>(type: "TEXT", nullable: true),
                    ActivateSceneId = table.Column<string>(type: "TEXT", nullable: true),
                    ActivateEventId = table.Column<string>(type: "TEXT", nullable: true),
                    Enemies = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Encounters");

            migrationBuilder.DropColumn(
                name: "Portrait",
                table: "Enemies");

            migrationBuilder.AddColumn<bool>(
                name: "Spotlight",
                table: "Enemies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
