using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monolith.Migrations
{
    /// <summary>
    ///     Idempotent repair of the Subscription schema: databases where AddSubscriptionBoundedContext is already
    ///     recorded in __EFMigrationsHistory but subscription_plan_changes and/or the plan seed are missing.
    ///     Safe to run on databases that already have everything.
    /// </summary>
    public partial class EnsureSubscriptionSchemaAndSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `subscription_plan_changes` (
    `id` char(36) NOT NULL,
    `previous_plan_code` varchar(20) NOT NULL,
    `target_plan_code` varchar(20) NOT NULL,
    `status` varchar(20) NOT NULL,
    `payment_reference` varchar(120) NULL,
    `checkout_url` varchar(500) NULL,
    `requested_by` char(36) NOT NULL,
    `requested_at` datetime(6) NOT NULL,
    `resolved_at` datetime(6) NULL,
    `resolution_detail` varchar(300) NULL,
    `organization_id` char(36) NOT NULL,
    `price_in_cents` bigint NOT NULL,
    `price_currency` varchar(3) NOT NULL,
    CONSTRAINT `p_k_subscription_plan_changes` PRIMARY KEY (`id`),
    CONSTRAINT `fk_subscription_plan_changes_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `subscription_tenants` (`organization_id`) ON DELETE CASCADE,
    INDEX `i_x_subscription_plan_changes_organization_id` (`organization_id`),
    UNIQUE INDEX `i_x_subscription_plan_changes_payment_reference` (`payment_reference`)
) CHARACTER SET=utf8mb4;");

            migrationBuilder.Sql(@"
INSERT IGNORE INTO `subscription_plans`
    (`code`, `name`, `description`, `monthly_price_in_cents`, `currency`, `max_active_routes`, `max_vehicles`, `is_default`, `is_available`)
VALUES
    ('BASIC', 'Plan Básico', 'Para conductores independientes que empiezan: hasta 3 rutas activas y 3 vehículos.', 0, 'PEN', 3, 3, 1, 1),
    ('COMPLETE', 'Plan Completo', 'Para pequeñas empresas de transporte: hasta 10 rutas activas y 10 vehículos.', 9990, 'PEN', 10, 10, 0, 1),
    ('PREMIUM', 'Plan Premium', 'Para operaciones grandes: hasta 30 rutas activas y 30 vehículos.', 19990, 'PEN', 30, 30, 0, 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: this migration only repairs state owned by AddSubscriptionBoundedContext,
            // whose Down() drops these objects.
        }
    }
}
