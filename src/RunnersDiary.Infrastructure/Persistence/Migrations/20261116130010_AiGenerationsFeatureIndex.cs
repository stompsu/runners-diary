using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiGenerationsFeatureIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGenerations_Created",
                table: "AiGenerations");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_Created",
                table: "AiGenerations",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "IsGuest", "Feature", "Attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGenerations_Created",
                table: "AiGenerations");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_Created",
                table: "AiGenerations",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "IsGuest" });
        }
    }
}
