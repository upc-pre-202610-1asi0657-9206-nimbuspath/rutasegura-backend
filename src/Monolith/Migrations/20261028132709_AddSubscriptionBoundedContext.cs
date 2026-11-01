using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Monolith.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBoundedContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payment_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    payment_reference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    received_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_subscription_payment_events", x => x.event_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    monthly_price_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    max_active_routes = table.Column<int>(type: "int", nullable: false),
                    max_vehicles = table.Column<int>(type: "int", nullable: false),
                    is_default = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_available = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_subscription_plans", x => x.code);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subscription_tenants",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    paid_until = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_subscription_tenants", x => x.organization_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subscription_plan_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    previous_plan_code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    target_plan_code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    payment_reference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    checkout_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    requested_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resolution_detail = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    organization_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    price_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    price_currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_subscription_plan_changes", x => x.id);
                    table.ForeignKey(
                        name: "f_k_subscription_plan_changes_subscription_tenants_organization_~",
                        column: x => x.organization_id,
                        principalTable: "subscription_tenants",
                        principalColumn: "organization_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "code", "currency", "description", "is_available", "is_default", "max_active_routes", "max_vehicles", "monthly_price_in_cents", "name" },
                values: new object[,]
                {
                    { "BASIC", "PEN", "Para conductores independientes que empiezan: hasta 3 rutas activas y 3 vehículos.", true, true, 3, 3, 0L, "Plan Básico" },
                    { "COMPLETE", "PEN", "Para pequeñas empresas de transporte: hasta 10 rutas activas y 10 vehículos.", true, false, 10, 10, 9990L, "Plan Completo" },
                    { "PREMIUM", "PEN", "Para operaciones grandes: hasta 30 rutas activas y 30 vehículos.", true, false, 30, 30, 19990L, "Plan Premium" }
                });

            migrationBuilder.CreateIndex(
                name: "i_x_subscription_payment_events_payment_reference",
                table: "subscription_payment_events",
                column: "payment_reference");

            migrationBuilder.CreateIndex(
                name: "i_x_subscription_plan_changes_organization_id",
                table: "subscription_plan_changes",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "i_x_subscription_plan_changes_payment_reference",
                table: "subscription_plan_changes",
                column: "payment_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payment_events");

            migrationBuilder.DropTable(
                name: "subscription_plan_changes");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "subscription_tenants");
        }
    }
}
