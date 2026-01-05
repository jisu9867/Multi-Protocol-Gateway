using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTelemetryEventSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "telemetry_events");

            migrationBuilder.RenameColumn(
                name: "Timestamp",
                table: "telemetry_events",
                newName: "timestamp");

            migrationBuilder.RenameColumn(
                name: "SourceId",
                table: "telemetry_events",
                newName: "source_id");

            migrationBuilder.RenameColumn(
                name: "Payload",
                table: "telemetry_events",
                newName: "value_json");

            migrationBuilder.RenameColumn(
                name: "AdapterId",
                table: "telemetry_events",
                newName: "tag");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_Timestamp",
                table: "telemetry_events",
                newName: "IX_telemetry_events_timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_SourceId",
                table: "telemetry_events",
                newName: "IX_telemetry_events_source_id");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_AdapterId",
                table: "telemetry_events",
                newName: "IX_telemetry_events_tag");

            // Add event_id as nullable first
            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "telemetry_events",
                type: "uuid",
                nullable: true);

            // Update existing rows to have unique GUIDs using PostgreSQL's gen_random_uuid()
            migrationBuilder.Sql(@"
                UPDATE telemetry_events 
                SET event_id = gen_random_uuid() 
                WHERE event_id IS NULL;
            ");

            // Now make it NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "telemetry_events",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "quality",
                table: "telemetry_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "route_key",
                table: "telemetry_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "telemetry_events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                table: "telemetry_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_event_id",
                table: "telemetry_events",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_events_source_timestamp",
                table: "telemetry_events",
                columns: new[] { "source_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_event_id",
                table: "telemetry_events");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_events_source_timestamp",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "quality",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "route_key",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "telemetry_events");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "telemetry_events");

            migrationBuilder.RenameColumn(
                name: "timestamp",
                table: "telemetry_events",
                newName: "Timestamp");

            migrationBuilder.RenameColumn(
                name: "source_id",
                table: "telemetry_events",
                newName: "SourceId");

            migrationBuilder.RenameColumn(
                name: "value_json",
                table: "telemetry_events",
                newName: "Payload");

            migrationBuilder.RenameColumn(
                name: "tag",
                table: "telemetry_events",
                newName: "AdapterId");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_timestamp",
                table: "telemetry_events",
                newName: "IX_telemetry_events_Timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_tag",
                table: "telemetry_events",
                newName: "IX_telemetry_events_AdapterId");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_events_source_id",
                table: "telemetry_events",
                newName: "IX_telemetry_events_SourceId");

            migrationBuilder.AddColumn<string>(
                name: "Id",
                table: "telemetry_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "Metadata",
                table: "telemetry_events",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_telemetry_events",
                table: "telemetry_events",
                column: "Id");
        }
    }
}
