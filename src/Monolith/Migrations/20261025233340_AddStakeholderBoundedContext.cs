using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monolith.Migrations
{
    /// <inheritdoc />
    public partial class AddStakeholderBoundedContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "route_stops",
                type: "varchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "MANUAL");

            migrationBuilder.AddColumn<string>(
                name: "enrollment_code",
                table: "route_itineraries",
                type: "varchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            // Routes created before this migration get a random enrollment code (same alphabet as RouteEnrollmentCode)
            // before the unique index is created.
            migrationBuilder.Sql(
                "UPDATE route_itineraries SET enrollment_code = CONCAT(ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9'), ELT(1 + FLOOR(RAND() * 31), 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '2', '3', '4', '5', '6', '7', '8', '9')) WHERE enrollment_code = '';");

            migrationBuilder.CreateTable(
                name: "stakeholder_guardian_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    document_number = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_guardian_profiles", x => x.user_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stakeholder_outbox_messages",
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
                    table.PrimaryKey("p_k_stakeholder_outbox_messages", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stakeholder_student_photos",
                columns: table => new
                {
                    student_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    content_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    content = table.Column<byte[]>(type: "mediumblob", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_student_photos", x => x.student_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stakeholder_students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    guardian_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    birth_date = table.Column<DateTime>(type: "date", nullable: true),
                    school_grade = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    school_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    removed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    photo_content_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    photo_size_bytes = table.Column<int>(type: "int", nullable: true),
                    photo_uploaded_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    photo_version = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    pickup_address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    pickup_latitude = table.Column<double>(type: "double", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double", nullable: false),
                    pickup_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_students", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stakeholder_route_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    route_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    enrolled_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    end_reason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    student_id = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_route_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_stakeholder_route_enrollments_student_id",
                        column: x => x.student_id,
                        principalTable: "stakeholder_students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "i_x_route_itineraries_enrollment_code",
                table: "route_itineraries",
                column: "enrollment_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_outbox_messages_processed_at_next_attempt_at",
                table: "stakeholder_outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_route_enrollments_organization_id_status",
                table: "stakeholder_route_enrollments",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_route_enrollments_route_id_status",
                table: "stakeholder_route_enrollments",
                columns: new[] { "route_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_route_enrollments_student_id",
                table: "stakeholder_route_enrollments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_students_guardian_user_id_status",
                table: "stakeholder_students",
                columns: new[] { "guardian_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stakeholder_guardian_profiles");

            migrationBuilder.DropTable(
                name: "stakeholder_outbox_messages");

            migrationBuilder.DropTable(
                name: "stakeholder_route_enrollments");

            migrationBuilder.DropTable(
                name: "stakeholder_student_photos");

            migrationBuilder.DropTable(
                name: "stakeholder_students");

            migrationBuilder.DropIndex(
                name: "i_x_route_itineraries_enrollment_code",
                table: "route_itineraries");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "route_stops");

            migrationBuilder.DropColumn(
                name: "enrollment_code",
                table: "route_itineraries");
        }
    }
}
