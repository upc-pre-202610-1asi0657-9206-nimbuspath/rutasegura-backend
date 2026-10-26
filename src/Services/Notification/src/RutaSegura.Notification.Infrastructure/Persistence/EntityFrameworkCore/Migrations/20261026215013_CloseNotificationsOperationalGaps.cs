using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class CloseNotificationsOperationalGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cycle_attempt_count",
                table: "notification_deliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "replay_count",
                table: "notification_deliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Conservar el presupuesto consumido por las entregas creadas antes de esta migracion.
            migrationBuilder.Sql("UPDATE notification_deliveries SET cycle_attempt_count = attempt_count");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_notification_deliveries_organization_id_id",
                table: "notification_deliveries",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateTable(
                name: "notification_delivery_replays",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    delivery_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    request_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    requested_by_user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    reason_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_bin"),
                    requested_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_delivery_replays", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_delivery_replays_notification_deliveries_organi~",
                        columns: x => new { x.organization_id, x.delivery_id },
                        principalTable: "notification_deliveries",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_notification_retention",
                table: "notification_notifications",
                columns: new[] { "read_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_delivery_replays_organization_id_delivery_id",
                table: "notification_delivery_replays",
                columns: new[] { "organization_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_replay_request",
                table: "notification_delivery_replays",
                columns: new[] { "organization_id", "request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery_replays");

            migrationBuilder.DropIndex(
                name: "ix_notification_retention",
                table: "notification_notifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_notification_deliveries_organization_id_id",
                table: "notification_deliveries");

            migrationBuilder.DropColumn(
                name: "cycle_attempt_count",
                table: "notification_deliveries");

            migrationBuilder.DropColumn(
                name: "replay_count",
                table: "notification_deliveries");
        }
    }
}
