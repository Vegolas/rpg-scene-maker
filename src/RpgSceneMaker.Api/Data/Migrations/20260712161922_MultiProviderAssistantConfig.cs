using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgSceneMaker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiProviderAssistantConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename in place (not drop/create) so the user's already-saved key survives, then add the new
            // provider selector defaulting to the previous behaviour ("anthropic").
            migrationBuilder.RenameTable(
                name: "AnthropicConfigs",
                newName: "AssistantConfigs");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AssistantConfigs",
                type: "TEXT",
                nullable: false,
                defaultValue: "anthropic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AssistantConfigs");

            migrationBuilder.RenameTable(
                name: "AssistantConfigs",
                newName: "AnthropicConfigs");
        }
    }
}
