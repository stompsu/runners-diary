using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunTypes",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    CountsTowardRunningDistance = table.Column<bool>(type: "bit", nullable: false),
                    CountsTowardLoad = table.Column<bool>(type: "bit", nullable: false),
                    IsRestDay = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TimeZoneIsDefault = table.Column<bool>(type: "bit", nullable: false),
                    UnitSystem = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Kind", "([Kind] = 1 AND [ExpiresAt] IS NOT NULL AND [ExternalTenantId] IS NULL AND [ExternalObjectId] IS NULL) OR ([Kind] = 0 AND [ExpiresAt] IS NULL AND [ExternalTenantId] IS NOT NULL AND [ExternalObjectId] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RunTypeId = table.Column<short>(type: "smallint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DistanceMeters = table.Column<double>(type: "float", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "float", nullable: true),
                    AvgHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MaxHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    Rpe = table.Column<byte>(type: "tinyint", nullable: true),
                    Gear = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Weather = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Surface = table.Column<byte>(type: "tinyint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Location_Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location_Point = table.Column<Point>(type: "geography", nullable: true),
                    Race_BibNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Race_CategoryName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Race_CategoryPlace = table.Column<int>(type: "int", nullable: true),
                    Race_DistanceCategory = table.Column<byte>(type: "tinyint", nullable: true),
                    Race_Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Race_OfficialDistanceMeters = table.Column<double>(type: "float", nullable: true),
                    Race_OverallPlace = table.Column<int>(type: "int", nullable: true),
                    Race_ResultTimeSeconds = table.Column<int>(type: "int", nullable: true),
                    Race_ResultsUrl = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: true),
                    Race_TargetTimeSeconds = table.Column<int>(type: "int", nullable: true),
                    Rating_Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Rating_Stars = table.Column<byte>(type: "tinyint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("AK_Runs_Id_UserId", x => new { x.Id, x.UserId });
                    table.ForeignKey(
                        name: "FK_Runs_RunTypes_RunTypeId",
                        column: x => x.RunTypeId,
                        principalTable: "RunTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Runs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GpxTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RawBlobName = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GpxTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GpxTracks_Runs_RunId_UserId",
                        columns: x => new { x.RunId, x.UserId },
                        principalTable: "Runs",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RunTypes",
                columns: new[] { "Id", "Category", "Code", "CountsTowardLoad", "CountsTowardRunningDistance", "IsRestDay", "SortOrder" },
                values: new object[,]
                {
                    { (short)1, (byte)0, "roadRace", true, true, false, (short)1 },
                    { (short)2, (byte)0, "trailRace", true, true, false, (short)2 },
                    { (short)3, (byte)0, "ultra", true, true, false, (short)3 },
                    { (short)4, (byte)0, "trackRace", true, true, false, (short)4 },
                    { (short)5, (byte)0, "crossCountry", true, true, false, (short)5 },
                    { (short)6, (byte)0, "virtualRace", true, true, false, (short)6 },
                    { (short)20, (byte)1, "easy", true, true, false, (short)7 },
                    { (short)21, (byte)1, "recovery", true, true, false, (short)8 },
                    { (short)22, (byte)1, "long", true, true, false, (short)9 },
                    { (short)23, (byte)1, "tempo", true, true, false, (short)10 },
                    { (short)24, (byte)1, "intervals", true, true, false, (short)11 },
                    { (short)25, (byte)1, "fartlek", true, true, false, (short)12 },
                    { (short)26, (byte)1, "hillRepeats", true, true, false, (short)13 },
                    { (short)27, (byte)1, "progression", true, true, false, (short)14 },
                    { (short)28, (byte)1, "racePace", true, true, false, (short)15 },
                    { (short)29, (byte)1, "stridesDrills", true, true, false, (short)16 },
                    { (short)30, (byte)1, "trailTraining", true, true, false, (short)17 },
                    { (short)31, (byte)1, "treadmill", true, true, false, (short)18 },
                    { (short)40, (byte)2, "crossTraining", true, false, false, (short)19 },
                    { (short)41, (byte)2, "rest", false, false, true, (short)20 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GpxTracks_RunId_UserId",
                table: "GpxTracks",
                columns: new[] { "RunId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GpxTracks_UserId",
                table: "GpxTracks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_GpxTracks_Run_Kind",
                table: "GpxTracks",
                columns: new[] { "RunId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "CX_Runs_User_Date",
                table: "Runs",
                columns: new[] { "UserId", "Date" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_RunTypeId",
                table: "Runs",
                column: "RunTypeId");

            migrationBuilder.CreateIndex(
                name: "UX_RunTypes_Code",
                table: "RunTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Guest_ExpiresAt",
                table: "Users",
                column: "ExpiresAt",
                filter: "[Kind] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Users_External",
                table: "Users",
                columns: new[] { "ExternalTenantId", "ExternalObjectId" },
                unique: true,
                filter: "[ExternalObjectId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GpxTracks");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "RunTypes");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
