using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telemetry_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AdapterId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    Metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_AdapterId",
                table: "telemetry_events",
                column: "AdapterId");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_SourceId",
                table: "telemetry_events",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_Timestamp",
                table: "telemetry_events",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_events");
        }
    }
}
