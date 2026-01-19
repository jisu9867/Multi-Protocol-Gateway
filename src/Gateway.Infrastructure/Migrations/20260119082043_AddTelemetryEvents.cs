using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,");

            migrationBuilder.CreateTable(
                name: "telemetry_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    factory_id = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    quality = table.Column<int>(type: "integer", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    route_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trace_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_events", x => new { x.event_id, x.timestamp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_equipment_name",
                table: "telemetry_events",
                column: "equipment_name");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_equipment_type",
                table: "telemetry_events",
                column: "equipment_type");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_factory_equipment_timestamp",
                table: "telemetry_events",
                columns: new[] { "factory_id", "equipment_type", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_factory_id",
                table: "telemetry_events",
                column: "factory_id");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_factory_timestamp",
                table: "telemetry_events",
                columns: new[] { "factory_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_source_id",
                table: "telemetry_events",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_source_timestamp",
                table: "telemetry_events",
                columns: new[] { "source_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_tag",
                table: "telemetry_events",
                column: "tag");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_timestamp",
                table: "telemetry_events",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_events");
        }
    }
}
