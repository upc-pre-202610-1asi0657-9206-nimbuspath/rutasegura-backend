using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace RutaSegura.TripTracking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "guardian_students",
                columns: table => new
                {
                    guardian_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    student_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    linked_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_students", x => new { x.guardian_id, x.student_id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    message_type = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    message_type = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    schema_version = table.Column<int>(type: "int", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    payload = table.Column<string>(type: "longtext", nullable: false),
                    correlation_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "route_snapshots",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    snapshot = table.Column<string>(type: "longtext", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_snapshots", x => x.route_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "track_points",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double", nullable: false),
                    speed_kmh = table.Column<double>(type: "double", nullable: true),
                    heading_degrees = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_points", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    driver_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    scheduled_start = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    cancellation_reason = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                    last_position_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_speed_kmh = table.Column<double>(type: "double", nullable: true),
                    last_heading_degrees = table.Column<double>(type: "double", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    processed_client_events = table.Column<string>(type: "longtext", nullable: false),
                    last_latitude = table.Column<double>(type: "double", nullable: true),
                    last_longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "trip_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    notify_guardians = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_incidents_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "trip_passengers",
                columns: table => new
                {
                    student_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    boarded_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    alighted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    absence_source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    absence_note = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                    alighted_latitude = table.Column<double>(type: "double", nullable: true),
                    alighted_longitude = table.Column<double>(type: "double", nullable: true),
                    boarded_latitude = table.Column<double>(type: "double", nullable: true),
                    boarded_longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_passengers", x => new { x.trip_id, x.student_id });
                    table.ForeignKey(
                        name: "fk_trip_passengers_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "trip_sos_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    raised_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resolved_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_sos_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_sos_alerts_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "trip_stops",
                columns: table => new
                {
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    reached_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    approach_notified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_stops", x => new { x.trip_id, x.stop_id });
                    table.ForeignKey(
                        name: "fk_trip_stops_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_guardian_students_student_id",
                table: "guardian_students",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at_occurred_at",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_track_points_trip_id_recorded_at",
                table: "track_points",
                columns: new[] { "trip_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_incidents_trip_id",
                table: "trip_incidents",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_passengers_student_id",
                table: "trip_passengers",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_sos_alerts_trip_id",
                table: "trip_sos_alerts",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "ix_trips_driver_id_scheduled_start",
                table: "trips",
                columns: new[] { "driver_id", "scheduled_start" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_organization_id_scheduled_start",
                table: "trips",
                columns: new[] { "organization_id", "scheduled_start" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_route_id_scheduled_start",
                table: "trips",
                columns: new[] { "route_id", "scheduled_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trips_status",
                table: "trips",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guardian_students");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "route_snapshots");

            migrationBuilder.DropTable(
                name: "track_points");

            migrationBuilder.DropTable(
                name: "trip_incidents");

            migrationBuilder.DropTable(
                name: "trip_passengers");

            migrationBuilder.DropTable(
                name: "trip_sos_alerts");

            migrationBuilder.DropTable(
                name: "trip_stops");

            migrationBuilder.DropTable(
                name: "trips");
        }
    }
}
