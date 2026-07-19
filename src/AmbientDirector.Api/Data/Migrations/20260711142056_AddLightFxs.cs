using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLightFxs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LightFxs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    CycleMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Keyframes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightFxs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LightFxs");
        }
    }
}
