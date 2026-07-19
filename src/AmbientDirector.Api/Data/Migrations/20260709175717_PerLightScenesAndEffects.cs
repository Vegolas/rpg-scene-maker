using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerLightScenesAndEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lights",
                table: "Scenes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Lights",
                table: "LightingConfigs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lights",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "Lights",
                table: "LightingConfigs");
        }
    }
}
