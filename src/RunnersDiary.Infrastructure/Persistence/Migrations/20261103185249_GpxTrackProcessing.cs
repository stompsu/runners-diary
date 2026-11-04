using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RunnersDiary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GpxTrackProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DistanceMeters",
                table: "GpxTracks",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ElapsedSeconds",
                table: "GpxTracks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ElevationGainMeters",
                table: "GpxTracks",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ElevationLossMeters",
                table: "GpxTracks",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndUtc",
                table: "GpxTracks",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "FinishPoint",
                table: "GpxTracks",
                type: "geography",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxElevationMeters",
                table: "GpxTracks",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinElevationMeters",
                table: "GpxTracks",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovingSeconds",
                table: "GpxTracks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointCount",
                table: "GpxTracks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "GpxTracks",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessedBlobName",
                table: "GpxTracks",
                type: "varchar(400)",
                unicode: false,
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ProcessorVersion",
                table: "GpxTracks",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "StartPoint",
                table: "GpxTracks",
                type: "geography",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartUtc",
                table: "GpxTracks",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "WarningFlags",
                table: "GpxTracks",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GpxTracks_Processed",
                table: "GpxTracks",
                sql: "[Status] <> 1 OR ([ProcessedBlobName] IS NOT NULL AND [DistanceMeters] IS NOT NULL AND [PointCount] IS NOT NULL AND [StartPoint] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GpxTracks_Processed",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "DistanceMeters",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ElapsedSeconds",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ElevationGainMeters",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ElevationLossMeters",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "EndUtc",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "FinishPoint",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "MaxElevationMeters",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "MinElevationMeters",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "MovingSeconds",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "PointCount",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ProcessedBlobName",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "ProcessorVersion",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "StartPoint",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "StartUtc",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "WarningFlags",
                table: "GpxTracks");
        }
    }
}
