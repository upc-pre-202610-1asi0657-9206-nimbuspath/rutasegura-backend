using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monolith.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedGuardianAccessAndRoadItineraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "distance_meters",
                table: "route_itineraries",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "duration_seconds",
                table: "route_itineraries",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geometry_geo_json",
                table: "route_itineraries",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "geometry_version",
                table: "route_itineraries",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "itinerary_calculated_at",
                table: "route_itineraries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stakeholder_guardian_access_grants",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    guardian_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    student_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false),
                    verification_reference = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_guardian_access_grants", x => new { x.organization_id, x.guardian_user_id, x.student_id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stakeholder_organization_guardians",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    document_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stakeholder_organization_guardians", x => new { x.organization_id, x.user_id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql(@"INSERT INTO stakeholder_guardian_access_grants
                (organization_id, guardian_user_id, student_id, status, requested_at, version)
                SELECT DISTINCT e.organization_id, s.guardian_user_id, s.id, 'PENDING', UTC_TIMESTAMP(6), 0
                FROM stakeholder_route_enrollments e JOIN stakeholder_students s ON s.id = e.student_id
                WHERE s.status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "i_x_stakeholder_guardian_access_grants_guardian_user_id_status",
                table: "stakeholder_guardian_access_grants",
                columns: new[] { "guardian_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stakeholder_guardian_access_grants");

            migrationBuilder.DropTable(
                name: "stakeholder_organization_guardians");

            migrationBuilder.DropColumn(
                name: "distance_meters",
                table: "route_itineraries");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "route_itineraries");

            migrationBuilder.DropColumn(
                name: "geometry_geo_json",
                table: "route_itineraries");

            migrationBuilder.DropColumn(
                name: "geometry_version",
                table: "route_itineraries");

            migrationBuilder.DropColumn(
                name: "itinerary_calculated_at",
                table: "route_itineraries");
        }
    }
}
