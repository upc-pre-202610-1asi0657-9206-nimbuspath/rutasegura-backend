using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monolith.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrationBoundedContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administration_inbox_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    message_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_administration_inbox_messages", x => x.message_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "administration_outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    message_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    schema_version = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("p_k_administration_outbox_messages", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "operations_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    type = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    severity = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    incident_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    category = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: true),
                    detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    affected_students = table.Column<int>(type: "int", nullable: false),
                    driver_user_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    acknowledged_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resolved_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_operations_alerts", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "operations_trip_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    trip_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    delivered_students = table.Column<int>(type: "int", nullable: false),
                    absent_students = table.Column<int>(type: "int", nullable: false),
                    incident_count = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_operations_trip_records", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "roster_drivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    document_number = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    phone_number = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    license_category = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    license_expires_on = table.Column<DateTime>(type: "date", nullable: false),
                    license_number = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_roster_drivers", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "roster_vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    license_plate = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false),
                    seating_capacity = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    brand = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_roster_vehicles", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "route_itineraries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    activated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    assigned_driver_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    assigned_vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    departure_time = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    estimated_arrival_time = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    operating_days_mask = table.Column<int>(type: "int", nullable: false),
                    time_zone_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_route_itineraries", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "route_stops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    student_ids = table.Column<string>(type: "longtext", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_route_stops", x => x.id);
                    table.ForeignKey(
                        name: "f_k_route_stops_route_itineraries_route_id",
                        column: x => x.route_id,
                        principalTable: "route_itineraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "i_x_administration_outbox_messages_processed_at_next_attempt_at",
                table: "administration_outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_operations_alerts_incident_id",
                table: "operations_alerts",
                column: "incident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_operations_alerts_organization_id_status",
                table: "operations_alerts",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_operations_alerts_trip_id",
                table: "operations_alerts",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "i_x_operations_trip_records_route_id",
                table: "operations_trip_records",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "i_x_operations_trip_records_trip_id",
                table: "operations_trip_records",
                column: "trip_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_roster_drivers_organization_id_document_number",
                table: "roster_drivers",
                columns: new[] { "organization_id", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_roster_drivers_user_id",
                table: "roster_drivers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_roster_vehicles_organization_id_license_plate",
                table: "roster_vehicles",
                columns: new[] { "organization_id", "license_plate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_route_itineraries_organization_id_name",
                table: "route_itineraries",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_route_itineraries_organization_id_status",
                table: "route_itineraries",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_route_stops_route_id",
                table: "route_stops",
                column: "route_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administration_inbox_messages");

            migrationBuilder.DropTable(
                name: "administration_outbox_messages");

            migrationBuilder.DropTable(
                name: "operations_alerts");

            migrationBuilder.DropTable(
                name: "operations_trip_records");

            migrationBuilder.DropTable(
                name: "roster_drivers");

            migrationBuilder.DropTable(
                name: "roster_vehicles");

            migrationBuilder.DropTable(
                name: "route_stops");

            migrationBuilder.DropTable(
                name: "route_itineraries");
        }
    }
}
