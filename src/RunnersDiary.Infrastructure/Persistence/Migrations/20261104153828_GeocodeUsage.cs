using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeocodeUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeocodeUsage",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeocodeUsage", x => x.Day);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeocodeUsage");
        }
    }
}
