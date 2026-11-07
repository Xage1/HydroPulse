using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydroPulse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryRecordSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RenewablePowerInputKw = table.Column<double>(type: "double precision", nullable: false),
                    HydrogenProducedKgPerHour = table.Column<double>(type: "double precision", nullable: false),
                    WaterConsumedLitersPerHour = table.Column<double>(type: "double precision", nullable: false),
                    ElectricityCostPerHour = table.Column<double>(type: "double precision", nullable: false),
                    OperationalStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRecords_StackId_Timestamp",
                table: "TelemetryRecords",
                columns: new[] { "StackId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryRecords");
        }
    }
}
