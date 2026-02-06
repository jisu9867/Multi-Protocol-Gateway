using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sensor_agg_10min",
                columns: table => new
                {
                    bucket = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    factory_id = table.Column<int>(type: "integer", nullable: false),
                    tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    avg_value = table.Column<decimal>(type: "numeric", nullable: false),
                    min_value = table.Column<decimal>(type: "numeric", nullable: false),
                    max_value = table.Column<decimal>(type: "numeric", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    last_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_agg_10min", x => new { x.bucket, x.factory_id, x.tag, x.equipment_type, x.equipment_name, x.source_id });
                });

            migrationBuilder.CreateTable(
                name: "sensor_agg_1hour",
                columns: table => new
                {
                    bucket = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    factory_id = table.Column<int>(type: "integer", nullable: false),
                    tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    equipment_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    avg_value = table.Column<decimal>(type: "numeric", nullable: false),
                    min_value = table.Column<decimal>(type: "numeric", nullable: false),
                    max_value = table.Column<decimal>(type: "numeric", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_agg_1hour", x => new { x.bucket, x.factory_id, x.tag, x.equipment_type, x.equipment_name, x.source_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sensor_agg_10min_bucket",
                table: "sensor_agg_10min",
                column: "bucket",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_sensor_agg_10min_factory_tag_bucket",
                table: "sensor_agg_10min",
                columns: new[] { "factory_id", "tag", "bucket" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_sensor_agg_10min_last_timestamp",
                table: "sensor_agg_10min",
                column: "last_timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_sensor_agg_1hour_bucket",
                table: "sensor_agg_1hour",
                column: "bucket",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_sensor_agg_1hour_factory_tag_bucket",
                table: "sensor_agg_1hour",
                columns: new[] { "factory_id", "tag", "bucket" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sensor_agg_10min");

            migrationBuilder.DropTable(
                name: "sensor_agg_1hour");
        }
    }
}
