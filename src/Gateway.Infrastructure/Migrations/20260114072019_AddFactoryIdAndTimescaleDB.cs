using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFactoryIdAndTimescaleDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,");
            
            // Enable TimescaleDB extension (required)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");

            migrationBuilder.AddColumn<string>(
                name: "equipment_name",
                table: "telemetry_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "equipment_type",
                table: "telemetry_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<int>(
                name: "factory_id",
                table: "telemetry_events",
                type: "integer",
                nullable: false,
                defaultValue: 1); // Default to Ulsan (1)

            // Update existing rows: set equipment_name to source_id if empty
            migrationBuilder.Sql(@"
                UPDATE telemetry_events 
                SET equipment_name = source_id 
                WHERE equipment_name = '';
            ");

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

            // Drop Primary Key and unique index on event_id before creating hypertable
            // TimescaleDB requires partitioning column (timestamp) to be part of unique constraints and primary keys
            migrationBuilder.DropPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_event_id",
                table: "telemetry_events");

            // Convert table to TimescaleDB hypertable
            // Note: This should be done after all columns and indexes are added, but before unique constraints
            migrationBuilder.Sql(@"
                SELECT create_hypertable('telemetry_events', 'timestamp', 
                    chunk_time_interval => INTERVAL '1 day',
                    if_not_exists => TRUE);
            ");

            // Recreate Primary Key as composite key (event_id, timestamp) after hypertable creation
            // This ensures uniqueness while satisfying TimescaleDB requirements
            migrationBuilder.AddPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events",
                columns: new[] { "event_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop composite Primary Key before dropping hypertable
            migrationBuilder.DropPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events");

            // Drop TimescaleDB hypertable (convert back to regular table)
            migrationBuilder.Sql(@"
                SELECT drop_hypertable('telemetry_events', if_exists => TRUE);
            ");

            // Recreate original Primary Key on event_id only
            migrationBuilder.AddPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events",
                column: "event_id");

            // Recreate original unique index on event_id only
            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_event_id",
                table: "telemetry_events",
                column: "event_id",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_equipment_name",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_equipment_type",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_factory_equipment_timestamp",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_factory_id",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_factory_timestamp",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "equipment_name",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "equipment_type",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "factory_id",
                table: "telemetry_events");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:timescaledb", ",,");
        }
    }
}
