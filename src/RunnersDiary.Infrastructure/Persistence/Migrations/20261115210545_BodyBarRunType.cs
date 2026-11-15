using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BodyBarRunType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RunTypes",
                keyColumn: "Id",
                keyValue: (short)40,
                column: "SortOrder",
                value: (short)20);

            migrationBuilder.UpdateData(
                table: "RunTypes",
                keyColumn: "Id",
                keyValue: (short)41,
                column: "SortOrder",
                value: (short)21);

            migrationBuilder.InsertData(
                table: "RunTypes",
                columns: new[] { "Id", "Category", "Code", "CountsTowardLoad", "CountsTowardRunningDistance", "IsRestDay", "SortOrder" },
                values: new object[] { (short)42, (byte)2, "bodyBar", true, false, false, (short)19 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RunTypes",
                keyColumn: "Id",
                keyValue: (short)42);

            migrationBuilder.UpdateData(
                table: "RunTypes",
                keyColumn: "Id",
                keyValue: (short)40,
                column: "SortOrder",
                value: (short)19);

            migrationBuilder.UpdateData(
                table: "RunTypes",
                keyColumn: "Id",
                keyValue: (short)41,
                column: "SortOrder",
                value: (short)20);
        }
    }
}
