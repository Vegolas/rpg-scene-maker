using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Screens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Tiles = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screens", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Screens");
        }
    }
}
