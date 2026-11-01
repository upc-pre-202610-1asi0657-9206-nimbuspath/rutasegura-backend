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
    Payments/         PaymentGatewayOptions, MercadoPago/MercadoPagoPaymentGateway, Sandbox/SandboxPaymentGateway
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
| Pasarela (sin JWT) | `POST /api/v1/subscription/payments/notifications` | Webhook firmado. Responde 200 (también a los repetidos) o 401 si la firma es inválida. Con 409 (conflicto) o 503 (no se pudo leer el pago), la pasarela reintenta |
| ADMIN (solo sandbox) | `POST /api/v1/subscription/sandbox/checkouts/{paymentReference}/complete` `{ "approved": true }` | Simula el pago en desarrollo y demos |

El API Gateway enruta `/api/v1/subscription/**` al monolito. La organización y el administrador siempre salen del JWT de Iam.

## Pasarela de pagos

El equipo eligió **Mercado Pago (Checkout Pro)**: cobra en soles (PEN), acepta tarjetas, Yape y otros medios locales, y tiene credenciales de prueba gratuitas. El contexto solo conoce el puerto `IPaymentGateway`, que tiene dos adaptadores y se elige con `Subscription:PaymentGateway:Provider`.

### Mercado Pago (`MercadoPago`)

1. **Checkout.** Crea una *preferencia* (`POST /checkout/preferences`):
   - `external_reference` es la referencia del cambio de plan (`mp_<id>`) y `X-Idempotency-Key` es el id del cambio.
   - El administrador paga en `init_point`, que se devuelve como `checkoutUrl`.
2. **Webhook.** Mercado Pago llama a `POST /api/v1/subscription/payments/notifications?data.id=…&type=payment`. El adaptador:
   - Verifica `x-signature`: HMAC-SHA256 de `id:{data.id};request-id:{x-request-id};ts:{ts};` con la clave secreta del webhook, comparada en tiempo constante. Una firma inválida responde 401 y no consulta nada.
   - Lee el pago desde la API (`GET /v1/payments/{id}`). **El estado y el monto salen siempre de Mercado Pago**, nunca del cuerpo de la petición.
   - Traduce el estado:
     - `approved` → aprobado (se valida el monto contra el precio del plan).
     - `rejected` o `cancelled` → rechazado.
     - Cualquier otro estado (`pending`, `in_process`, `refunded`, …) → `PENDING`: se registra y no cambia nada.
   - Usa como id de evento `mp_<pago>_<estado>`. Mercado Pago envía varias notificaciones por pago, y solo la primera con cada estado tiene efecto.
   - Si no puede consultar la API, responde 503 y Mercado Pago reintenta.
3. Con este proveedor **no existen pagos simulados**: el endpoint `sandbox/…` responde 403.

**Solo se usan credenciales de PRUEBA** (cuentas y tarjetas de prueba de Mercado Pago, sin dinero real).

### Sandbox (`Sandbox`, por defecto)

Es el adaptador para desarrollo, demos sin cuenta y pruebas:

- Crea checkouts con referencia `sbx_…` y no cobra nada.
- Acepta su propio JSON firmado con HMAC-SHA256 (`X-Payment-Signature: sha256=<hex>`).
- Con `AllowSimulatedPayments`, un administrador puede aprobar o rechazar su checkout. La notificación pasa por la misma verificación e idempotencia que una real.

### Configuración (`Subscription:PaymentGateway`)

| Clave | Por defecto | Nota |
| :--- | :--- | :--- |
| `Provider` | `Sandbox` | `MercadoPago` o `Sandbox` |
| `WebhookSecret` | vacío | **Nunca en el repositorio.** Con Mercado Pago es la *clave secreta* del webhook y es obligatoria. Con el sandbox, vacío significa un secreto aleatorio por proceso |
| `AllowSimulatedPayments` | `false` (`true` solo en `appsettings.Development.json`) | Solo sandbox. **No activar en producción** |
| `CheckoutBaseUrl` | vacío | Solo sandbox: página del frontend que muestra el checkout |
| `MercadoPago:AccessToken` | vacío | **Nunca en el repositorio.** Es el Access Token de las credenciales de prueba (`TEST-…`) |
| `MercadoPago:NotificationUrl` | vacío | URL **https pública** del webhook a través del API Gateway. Mercado Pago no llega a `localhost` |
| `MercadoPago:ReturnUrl` | vacío | Página del frontend a la que vuelve el administrador tras pagar |
| `MercadoPago:StatementDescriptor` | `RUTASEGURA` | Texto que aparece en el estado de cuenta |
| `MercadoPago:ApiBaseUrl`, `TimeoutSeconds` | `https://api.mercadopago.com/`, `10` | |

Con `Provider = MercadoPago`, el monolito **no arranca** si falta el Access Token, la clave del webhook o la URL https de notificación.

### Probar con Mercado Pago (modo prueba)

1. En [Tus integraciones](https://www.mercadopago.com.pe/developers/panel/app), crea una aplicación de tipo *Checkout Pro* y copia el **Access Token de prueba**.
2. En la aplicación, ve a **Webhooks**:
   - Configura la URL `https://<api-gateway>/api/v1/subscription/payments/notifications` con el evento *Pagos*.
   - Copia la **clave secreta**.
3. Configura los valores **fuera del repositorio**. En local:

   ```bash
   cd src/Monolith
   dotnet user-secrets set "Subscription:PaymentGateway:Provider" "MercadoPago"
   dotnet user-secrets set "Subscription:PaymentGateway:WebhookSecret" "<clave secreta del webhook>"
   dotnet user-secrets set "Subscription:PaymentGateway:MercadoPago:AccessToken" "<Access Token de prueba>"
   dotnet user-secrets set "Subscription:PaymentGateway:MercadoPago:NotificationUrl" "https://<url pública>/api/v1/subscription/payments/notifications"
   ```

   En AWS, usa las variables de entorno `Subscription__PaymentGateway__Provider`, `Subscription__PaymentGateway__WebhookSecret`, `Subscription__PaymentGateway__MercadoPago__AccessToken` y `Subscription__PaymentGateway__MercadoPago__NotificationUrl`.
4. En local hace falta un túnel (por ejemplo `ngrok http <puerto del gateway>`) para que Mercado Pago alcance el webhook.
5. Ejecuta `POST /api/v1/subscription/plan-changes` y abre el `checkoutUrl`. Paga con un **comprador de prueba** y una **tarjeta de prueba** de Mercado Pago:
   - Titular `APRO` para un pago aprobado.
   - Titular `OTHE` para un pago rechazado.
6. Revisa el resultado con `GET /api/v1/subscription`: el plan vigente cambia solo cuando llega el webhook con el pago aprobado.

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

- `tests/RutaSegura.Monolith.Subscription.Tests`:
  - Dominio.
  - Casos de uso con dobles en memoria.
  - Adaptador sandbox (firmas).
  - Adaptador de Mercado Pago contra una API simulada: preferencia enviada, `x-signature`, traducción de estados y caídas. **Ninguna prueba llama a Mercado Pago.**
  - Reglas de arquitectura: capas, y que los demás contextos solo usen la fachada.
- `tests/RutaSegura.Monolith.Administration.IntegrationTests/SubscriptionEndToEndTests`: monolito real. Cubre catálogo público, cambio pagado que sube el límite de rutas solo tras verificar el pago, webhook firmado e idempotente, pago rechazado o incompleto, permisos y desactivación de los pagos simulados.

## Pendiente

- Probar el flujo completo con las credenciales de prueba de Mercado Pago, sobre el monolito desplegado o mediante un túnel.
- Reembolsos y contracargos de Mercado Pago (`refunded`, `charged_back`): hoy se registran como `PENDING` y no cambian el contrato.
- Aplicar `MaxVehicles` en Roster al registrar vehículos (hoy solo se expone en la fachada).
- Renovación mensual y aviso al administrador antes de que venza `paidUntil` (por ejemplo, un evento hacia Notification).
- Reembolso o revisión manual de un pago confirmado después de cancelar o rechazar el cambio. Queda registrado con resolución `ALREADY_RESOLVED`.
