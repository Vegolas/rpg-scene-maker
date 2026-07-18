using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgSceneMaker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingDoneUtc",
                table: "LightingConfigs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingDoneUtc",
                table: "LightingConfigs");
        }
    }
}
