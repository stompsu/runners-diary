using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaceCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CatalogRaceId",
                table: "Runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RaceInterestProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kinds = table.Column<byte>(type: "tinyint", nullable: false),
                    RegionCodes = table.Column<string>(type: "varchar(200)", nullable: false),
                    Center = table.Column<Point>(type: "geography", nullable: true),
                    CenterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RadiusMeters = table.Column<int>(type: "int", nullable: true),
                    MinDistanceMeters = table.Column<double>(type: "float", nullable: true),
                    MaxDistanceMeters = table.Column<double>(type: "float", nullable: true),
                    HorizonDays = table.Column<short>(type: "smallint", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShowInCalendar = table.Column<bool>(type: "bit", nullable: false),
                    ProposalsSeenUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceInterestProfiles", x => x.UserId);
                    table.CheckConstraint("CK_RaceInterestProfiles_Center", "([Center] IS NULL AND [RadiusMeters] IS NULL AND [CenterName] IS NULL) OR ([Center] IS NOT NULL AND [RadiusMeters] IS NOT NULL)");
                    table.CheckConstraint("CK_RaceInterestProfiles_Distance", "([MinDistanceMeters] IS NULL OR [MinDistanceMeters] BETWEEN 1000 AND 250000) AND ([MaxDistanceMeters] IS NULL OR [MaxDistanceMeters] BETWEEN 1000 AND 250000) AND ([MinDistanceMeters] IS NULL OR [MaxDistanceMeters] IS NULL OR [MinDistanceMeters] <= [MaxDistanceMeters])");
                    table.CheckConstraint("CK_RaceInterestProfiles_Horizon", "[HorizonDays] BETWEEN 7 AND 365");
                    table.CheckConstraint("CK_RaceInterestProfiles_Kinds", "[Kinds] BETWEEN 1 AND 127");
                    table.CheckConstraint("CK_RaceInterestProfiles_Radius", "[RadiusMeters] IS NULL OR [RadiusMeters] BETWEEN 5000 AND 300000");
                    table.ForeignKey(
                        name: "FK_RaceInterestProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Point = table.Column<Point>(type: "geography", nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    RegionCode = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: true),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Surface = table.Column<byte>(type: "tinyint", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    SignupUrl = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: true),
                    EventUrl = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: true),
                    SourceUrl = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: true),
                    PriceAmount = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    PriceCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    SignupDeadline = table.Column<DateOnly>(type: "date", nullable: true),
                    OrganizerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FactsHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ImportedUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    PreviousDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DateChangedUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    MissedScans = table.Column<byte>(type: "tinyint", nullable: false),
                    RemovedUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    DuplicateOfRaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DetailETag = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    DetailLastModified = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    DetailFetchedUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_Races_Duplicate", "[DuplicateOfRaceId] IS NULL OR [DuplicateOfRaceId] <> [Id]");
                    table.CheckConstraint("CK_Races_Price", "([PriceAmount] IS NULL AND [PriceCurrency] IS NULL) OR ([PriceAmount] IS NOT NULL AND [PriceCurrency] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Races_Races_DuplicateOfRaceId",
                        column: x => x.DuplicateOfRaceId,
                        principalTable: "Races",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RaceDistances",
                columns: table => new
                {
                    RaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceDistances", x => new { x.RaceId, x.DistanceMeters });
                    table.ForeignKey(
                        name: "FK_RaceDistances_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaceProposalStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    RaceDateWhenMarked = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceProposalStates", x => new { x.UserId, x.RaceId });
                    table.CheckConstraint("CK_RaceProposalStates_State", "[State] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_RaceProposalStates_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RaceProposalStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_CatalogRaceId",
                table: "Runs",
                column: "CatalogRaceId");

            migrationBuilder.CreateIndex(
                name: "UX_Runs_User_CatalogRace",
                table: "Runs",
                columns: new[] { "UserId", "CatalogRaceId" },
                unique: true,
                filter: "[CatalogRaceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RaceProposalStates_RaceId",
                table: "RaceProposalStates",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "CX_Races_Date",
                table: "Races",
                column: "Date")
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Races_DuplicateOf",
                table: "Races",
                column: "DuplicateOfRaceId",
                filter: "[DuplicateOfRaceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Races_Source_External",
                table: "Races",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_Races_CatalogRaceId",
                table: "Runs",
                column: "CatalogRaceId",
                principalTable: "Races",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runs_Races_CatalogRaceId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "RaceDistances");

            migrationBuilder.DropTable(
                name: "RaceInterestProfiles");

            migrationBuilder.DropTable(
                name: "RaceProposalStates");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropIndex(
                name: "IX_Runs_CatalogRaceId",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "UX_Runs_User_CatalogRace",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "CatalogRaceId",
                table: "Runs");
        }
    }
}
