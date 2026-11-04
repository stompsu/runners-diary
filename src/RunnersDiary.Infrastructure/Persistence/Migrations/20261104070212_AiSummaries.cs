using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsGuest = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    Attempts = table.Column<byte>(type: "tinyint", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    PromptVersion = table.Column<short>(type: "smallint", nullable: false),
                    ModelDeployment = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerations", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "AiSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Compare = table.Column<byte>(type: "tinyint", nullable: false),
                    Language = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    MetricsVersion = table.Column<short>(type: "smallint", nullable: false),
                    FactSheetVersion = table.Column<short>(type: "smallint", nullable: false),
                    PromptVersion = table.Column<short>(type: "smallint", nullable: false),
                    ModelDeployment = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Fingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSummaries", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_AiSummaries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "CX_AiGenerations_User_Created",
                table: "AiGenerations",
                columns: new[] { "UserId", "CreatedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_Created",
                table: "AiGenerations",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "IsGuest" });

            migrationBuilder.CreateIndex(
                name: "UX_AiSummaries_Key",
                table: "AiSummaries",
                columns: new[] { "UserId", "PeriodFrom", "PeriodTo", "Compare", "Language" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGenerations");

            migrationBuilder.DropTable(
                name: "AiSummaries");
        }
    }
}
