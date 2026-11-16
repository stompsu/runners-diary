using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Feature",
                table: "AiGenerations",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    Surface = table.Column<byte>(type: "tinyint", nullable: true),
                    EventName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RaceRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PlanGeneratedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    PlanGenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanNotes = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    PlanPromptVersion = table.Column<short>(type: "smallint", nullable: true),
                    PlanModelDeployment = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PlanModelId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    PlanHorizonEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanHorizonCapped = table.Column<bool>(type: "bit", nullable: false),
                    PlanOutdated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Location_Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location_Point = table.Column<Point>(type: "geography", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("AK_Goals_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_Goals_Distance", "[DistanceMeters] BETWEEN 1000 AND 250000");
                    table.CheckConstraint("CK_Goals_Elevation", "[ElevationGainMeters] IS NULL OR [ElevationGainMeters] BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_Goals_Status", "[Status] IN (0, 1, 2)");
                    table.CheckConstraint("CK_Goals_Target", "([Type] <> 2 OR [TargetDurationSeconds] IS NULL) AND ([Type] <> 1 OR [TargetDurationSeconds] IS NOT NULL)");
                    table.CheckConstraint("CK_Goals_Type", "[Type] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_Goals_Runs_RaceRunId_UserId",
                        columns: x => new { x.RaceRunId, x.UserId },
                        principalTable: "Runs",
                        principalColumns: new[] { "Id", "UserId" });
                    table.ForeignKey(
                        name: "FK_Goals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RunTypeId = table.Column<short>(type: "smallint", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    Intensity = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetPaceMinSecondsPerKm = table.Column<short>(type: "smallint", nullable: true),
                    TargetPaceMaxSecondsPerKm = table.Column<short>(type: "smallint", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Phase = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSuggestions", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_TrainingSuggestions_Pace", "([TargetPaceMinSecondsPerKm] IS NULL AND [TargetPaceMaxSecondsPerKm] IS NULL) OR ([TargetPaceMinSecondsPerKm] IS NOT NULL AND [TargetPaceMaxSecondsPerKm] IS NOT NULL AND [TargetPaceMinSecondsPerKm] <= [TargetPaceMaxSecondsPerKm])");
                    table.CheckConstraint("CK_TrainingSuggestions_Status", "[Status] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_TrainingSuggestions_Goals_GoalId_UserId",
                        columns: x => new { x.GoalId, x.UserId },
                        principalTable: "Goals",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingSuggestions_RunTypes_RunTypeId",
                        column: x => x.RunTypeId,
                        principalTable: "RunTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_GoalId_UserId",
                table: "Runs",
                columns: new[] { "GoalId", "UserId" },
                filter: "[GoalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "CX_Goals_User_TargetDate",
                table: "Goals",
                columns: new[] { "UserId", "TargetDate" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Goals_RaceRunId_UserId",
                table: "Goals",
                columns: new[] { "RaceRunId", "UserId" },
                filter: "[RaceRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "CX_TrainingSuggestions_User_Date",
                table: "TrainingSuggestions",
                columns: new[] { "UserId", "Date" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSuggestions_GoalId_UserId",
                table: "TrainingSuggestions",
                columns: new[] { "GoalId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSuggestions_RunTypeId",
                table: "TrainingSuggestions",
                column: "RunTypeId");

            migrationBuilder.CreateIndex(
                name: "UX_TrainingSuggestions_Goal_Date",
                table: "TrainingSuggestions",
                columns: new[] { "GoalId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_Goals_GoalId_UserId",
                table: "Runs",
                columns: new[] { "GoalId", "UserId" },
                principalTable: "Goals",
                principalColumns: new[] { "Id", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runs_Goals_GoalId_UserId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "TrainingSuggestions");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Runs_GoalId_UserId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Feature",
                table: "AiGenerations");
        }
    }
}
