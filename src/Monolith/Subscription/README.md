# Bounded Context Subscription (monolito)

Catálogo de planes B2B, el contrato de cada empresa de transporte (tenant) y los cambios de plan pagados mediante la pasarela de pagos. Implementa **US-07** ("Contratar y cambiar un plan B2B") y las decisiones de la Iteración 5 del informe: **AD-39**, **AD-40** y **ADR-05.4**.

Route Planning consulta los límites vigentes mediante la fachada `ISubscriptionContextFacade` (relación Customer/Supplier). Nunca lee las tablas de Subscription.

## Capas

```text
Subscription/
  Domain/
    Model/
      Aggregates/    Plan, TenantSubscription (+ PaymentResolution), ProcessedPaymentEvent
      Entities/      PlanChange
      ValueObjects/  PlanCode, Money (céntimos + ISO 4217), PlanChangeStatus
      Commands/, Queries/, Exceptions/, SubscriptionError.cs
    Repositories/    IPlanRepository, ITenantSubscriptionRepository, IProcessedPaymentEventRepository
  Application/
    CommandServices/ ISubscriptionCommandService (+ PaymentNotificationReceipt)
    QueryServices/   ISubscriptionQueryService, SubscriptionView, EffectivePlanLimits
    Internal/
      CommandServices/   SubscriptionCommandService
      QueryServices/     SubscriptionQueryService
      OutboundServices/  puertos: IPaymentGateway, ISimulatedPaymentNotifier, ISubscriptionUnitOfWork
  Infrastructure/
    Persistence/EFC/  ModelBuilderExtensions (ApplySubscriptionConfiguration), PlanCatalog (semilla), repositorios
    Payments/         PaymentGatewayOptions, Sandbox/SandboxPaymentGateway
    Configuration/    AddSubscriptionBoundedContext
  Interfaces/
    ACL/              ISubscriptionContextFacade (lenguaje publicado para otros contextos)
    Rest/             SubscriptionController, PaymentNotificationsController, Resources/, Transform/
```

## Reglas (ADR-05.4)

| Regla | Dónde | Código de error |
| :--- | :--- | :--- |
| Toda organización empieza en el plan por defecto (gratuito) | `TenantSubscription` | — |
| Un plan de pago queda en `PENDING_PAYMENT` y **no cambia los límites** hasta verificar el pago (US-07 S1) | `RequestPlanChange` | — |
| Un plan gratuito se aplica al instante, sin pasarela | `RequestPlanChange` | — |
| Solo un cambio pendiente a la vez | `RequestPlanChange` | `PlanChangeAlreadyPending` (409) |
| No se puede pedir el plan que ya se tiene ni uno retirado del catálogo | `RequestPlanChange` | `PlanAlreadyContracted`, `PlanNotAvailable` (409) |
| El pago confirmado aplica el plan **una sola vez** y por un mes (US-07 S2) | `ConfirmPayment` | — |
| Una confirmación repetida, tardía o tras un rechazo no aplica nada (US-07 S3) | `ConfirmPayment` | resolución `ALREADY_APPLIED` / `ALREADY_RESOLVED` |
| Si el monto o la moneda pagados no son el precio del plan, el cambio se rechaza | `ConfirmPayment` | resolución `AMOUNT_MISMATCH` |
| Al vencer el periodo pagado, rigen los límites del plan por defecto | `EffectivePlanCode` | — |
| La pasarela se llama **fuera de transacciones**; si no responde, el cambio se cancela y se puede reintentar | `SubscriptionCommandService` | `PaymentGatewayUnavailable` (503) |
| Cada notificación se registra por su `eventId` (Idempotent Consumer); una repetida se responde 200 sin efectos | `ProcessedPaymentEvent` (PK) | resolución `DUPLICATE` |
| Dos cambios concurrentes del mismo contrato: gana uno (`Version` como token de concurrencia) | `TenantSubscription.Version` | `ConcurrencyConflict` (409) |

Estados de un cambio: `PENDING_PAYMENT` → `APPLIED` | `REJECTED` | `CANCELLED` (estos tres son finales).

## API

| Rol | Endpoint | Uso |
| :--- | :--- | :--- |
| Público | `GET /api/v1/subscription/plans` | Lista de precios (landing y web) |
| ADMIN | `GET /api/v1/subscription` | Plan contratado, plan vigente, `paidUntil` y cambio pendiente |
| ADMIN | `POST /api/v1/subscription/plan-changes` `{ "targetPlanId": "COMPLETE" }` | 202 `PENDING_PAYMENT` con `paymentReference` y `checkoutUrl`; 200 `APPLIED` si el plan es gratuito |
| ADMIN | `GET /api/v1/subscription/plan-changes/{id}` | Seguir el pago |
| ADMIN | `POST /api/v1/subscription/plan-changes/{id}/cancel` | Retirar un cambio pendiente |
| Pasarela (sin JWT) | `POST /api/v1/subscription/payments/notifications` | Webhook firmado: 200 (también repetidos), 401 firma inválida, 409 conflicto (la pasarela reintenta) |
| ADMIN (solo sandbox) | `POST /api/v1/subscription/sandbox/checkouts/{paymentReference}/complete` `{ "approved": true }` | Simula el pago en desarrollo y demos |

El API Gateway enruta `/api/v1/subscription/**` al monolito. La organización y el administrador siempre salen del JWT de Iam.

## Pasarela de pagos

**El informe no fija un proveedor.** Solo define el puerto `IPaymentGateway` y habla del "adaptador de la pasarela seleccionada". El diagrama de contexto (4.1.3) menciona a Stripe como ejemplo. Por eso el contexto incluye un adaptador **Sandbox**:

- Crea checkouts con referencia `sbx_…` y no cobra nada.
- Acepta notificaciones en JSON firmadas con **HMAC-SHA256** del cuerpo exacto (`X-Payment-Signature: sha256=<hex>`), el mismo mecanismo que usan los webhooks de los proveedores reales. La comparación es en tiempo constante.
- Con `AllowSimulatedPayments` un administrador puede aprobar o rechazar su propio checkout. La notificación pasa por la misma verificación e idempotencia que una real.

Para usar un proveedor real (Stripe, Culqi, Mercado Pago, Niubiz…), basta con un nuevo adaptador de `IPaymentGateway` (checkout + verificación de su webhook) y registrarlo en `AddSubscriptionBoundedContext`. Ni el dominio ni la aplicación cambian.

### Configuración (`Subscription:PaymentGateway`)

| Clave | Por defecto | Nota |
| :--- | :--- | :--- |
| `Provider` | `Sandbox` | Único adaptador disponible por ahora |
| `WebhookSecret` | vacío | **Nunca en el repositorio**: `Subscription__PaymentGateway__WebhookSecret`. Vacío = secreto aleatorio por proceso (solo valen las notificaciones que el propio sandbox genera) |
| `AllowSimulatedPayments` | `false` (`true` solo en `appsettings.Development.json`) | **No activar en producción**: permitiría obtener un plan pagado sin pagar |
| `CheckoutBaseUrl` | vacío | Página del frontend que muestra el checkout; se le añade la referencia |

## Persistencia (migración `AddSubscriptionBoundedContext`)

| Tabla | Contenido |
| :--- | :--- |
| `subscription_plans` | Catálogo (semilla de la migración) |
| `subscription_tenants` | Contrato por organización (PK `organization_id`, `version` de concurrencia) |
| `subscription_plan_changes` | Cambios de plan e historial; `payment_reference` única |
| `subscription_payment_events` | Notificaciones procesadas (PK `event_id`) |

Catálogo inicial (`PlanCatalog`). Los precios y límites de los planes de pago son **propuestas por confirmar con el equipo**:

| Plan | Precio mensual | Rutas activas | Vehículos |
| :--- | :--- | :--- | :--- |
| `BASIC` Plan Básico (por defecto) | S/ 0.00 | 3 | 3 |
| `COMPLETE` Plan Completo | S/ 99.90 | 10 | 10 |
| `PREMIUM` Plan Premium | S/ 199.90 | 30 | 30 |

`BASIC` conserva el límite de 3 rutas activas que el monolito usaba antes, cuando lo leía de `Administration:Plans` (esa sección se eliminó).

## Pruebas

- `tests/RutaSegura.Monolith.Subscription.Tests`: dominio, casos de uso con dobles en memoria, adaptador sandbox (firmas) y reglas de arquitectura (capas, y que los demás contextos solo usen la fachada).
- `tests/RutaSegura.Monolith.Administration.IntegrationTests/SubscriptionEndToEndTests`: monolito real. Cubre catálogo público, cambio pagado que sube el límite de rutas solo tras verificar el pago, webhook firmado e idempotente, pago rechazado o incompleto, permisos y desactivación de los pagos simulados.

## Pendiente

- Elegir el proveedor real y su adaptador. Para el mercado peruano en soles conviene evaluar Culqi, Niubiz o Mercado Pago, además de Stripe.
- Aplicar `MaxVehicles` en Roster al registrar vehículos (hoy solo se expone en la fachada).
- Renovación mensual y aviso al administrador antes de que venza `paidUntil` (por ejemplo, un evento hacia Notification).
- Reembolso o revisión manual de un pago confirmado después de cancelar o rechazar el cambio. Queda registrado con resolución `ALREADY_RESOLVED`.
