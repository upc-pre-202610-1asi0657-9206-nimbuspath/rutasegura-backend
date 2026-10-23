using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_announcements",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    author_user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    request_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    route_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true, collation: "utf8mb4_bin"),
                    title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_bin"),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_bin"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_announcements", x => x.id);
                    table.UniqueConstraint("AK_notification_announcements_organization_id_id", x => new { x.organization_id, x.id });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_processed_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    consumer = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_bin"),
                    message_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    message_type = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_bin"),
                    payload_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin"),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    notification_count = table.Column<int>(type: "int", nullable: false),
                    skipped_reason = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_processed_events", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_push_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    installation_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    token_ciphertext = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_bin"),
                    platform = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    is_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_push_subscriptions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    recipient_user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    source_message_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    source_message_type = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_bin"),
                    correlation_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_bin"),
                    trip_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true, collation: "utf8mb4_bin"),
                    student_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true, collation: "utf8mb4_bin"),
                    subject_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    announcement_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true, collation: "utf8mb4_bin"),
                    category = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_bin"),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_bin"),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_notifications", x => x.id);
                    table.UniqueConstraint("AK_notification_notifications_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_notification_subject", "`subject_id` = COALESCE(`student_id`, '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "FK_notification_notifications_notification_announcements_organi~",
                        columns: x => new { x.organization_id, x.announcement_id },
                        principalTable: "notification_announcements",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    notification_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    channel = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    destination_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_error_code = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_bin"),
                    provider_message_id = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_bin"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_deliveries_notification_notifications_organizat~",
                        columns: x => new { x.organization_id, x.notification_id },
                        principalTable: "notification_notifications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_dispatch_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    organization_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    notification_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_bin"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expanded_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_dispatch_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_dispatch_jobs_notification_notifications_organi~",
                        columns: x => new { x.organization_id, x.notification_id },
                        principalTable: "notification_notifications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_announcement_request",
                table: "notification_announcements",
                columns: new[] { "organization_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_due",
                table: "notification_deliveries",
                columns: new[] { "status", "next_attempt_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_destination",
                table: "notification_deliveries",
                columns: new[] { "organization_id", "notification_id", "channel", "destination_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_pending",
                table: "notification_dispatch_jobs",
                columns: new[] { "expanded_at", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_dispatch_notification",
                table: "notification_dispatch_jobs",
                columns: new[] { "organization_id", "notification_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_inbox",
                table: "notification_notifications",
                columns: new[] { "organization_id", "recipient_user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_notifications_organization_id_announcement_id",
                table: "notification_notifications",
                columns: new[] { "organization_id", "announcement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_unread",
                table: "notification_notifications",
                columns: new[] { "organization_id", "recipient_user_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_source_recipient_subject",
                table: "notification_notifications",
                columns: new[] { "organization_id", "source_message_id", "source_message_type", "recipient_user_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_at",
                table: "notification_processed_events",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ux_processed_consumer_message",
                table: "notification_processed_events",
                columns: new[] { "consumer", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_recipient",
                table: "notification_push_subscriptions",
                columns: new[] { "organization_id", "user_id", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ux_push_installation",
                table: "notification_push_subscriptions",
                columns: new[] { "organization_id", "installation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_push_token_hash",
                table: "notification_push_subscriptions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_dispatch_jobs");

            migrationBuilder.DropTable(
                name: "notification_processed_events");

            migrationBuilder.DropTable(
                name: "notification_push_subscriptions");

            migrationBuilder.DropTable(
                name: "notification_notifications");

            migrationBuilder.DropTable(
                name: "notification_announcements");
        }
    }
}
