using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AQUAR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceId = table.Column<int>(type: "int", nullable: false),
                    Event = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: true),
                    CompanyLatitude = table.Column<double>(type: "float", nullable: true),
                    CompanyLongitude = table.Column<double>(type: "float", nullable: true),
                    RadiusMeters = table.Column<double>(type: "float", nullable: false),
                    VerificationReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceLocations_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceLocationAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceLocationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceLocationAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceLocationAlerts_AttendanceLocations_AttendanceLocationId",
                        column: x => x.AttendanceLocationId,
                        principalTable: "AttendanceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLocationAlerts_AttendanceLocationId",
                table: "AttendanceLocationAlerts",
                column: "AttendanceLocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLocations_AttendanceId_Event",
                table: "AttendanceLocations",
                columns: new[] { "AttendanceId", "Event" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceLocationAlerts");

            migrationBuilder.DropTable(
                name: "AttendanceLocations");
        }
    }
}
