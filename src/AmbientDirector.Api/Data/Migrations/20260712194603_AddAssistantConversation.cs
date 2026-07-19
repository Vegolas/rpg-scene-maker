using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmbientDirector.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TranscriptJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantConversations");
        }
    }
}
