using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SoundWaveform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Waveform",
                table: "Sounds",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Waveform",
                table: "Sounds");
        }
    }
}
