# RutaSegura · API Gateway (YARP)

Punto de entrada único para la app del conductor, la app del tutor y la web de la empresa. Las apps configuran **una
sola URL base**; el Gateway reenvía cada ruta a su servicio sin cambiarla (ruta, query, cuerpo y token).

| Ruta pública | Servicio |
|---|---|
| `/api/v1/users/**` | Monolito · Iam (sign-in, sign-up de padres, `/me`, conductores) |
| `/api/v1/organizations/{id}` (solo `GET` y `PUT`) | Monolito · Iam |
| `/api/v1/roster/**`, `/api/v1/routes/**`, `/api/v1/operations/**` | Monolito · Administration |
| `/api/v1/stakeholders/**` | Monolito · Stakeholder (padres, hijos, foto, dirección de recojo, inscripciones) |
| `/api/v1/trips/**`, `/api/v1/tracking/**`, `/api/v1/drivers/**`, `/api/v1/guardians/**` | Trip & Tracking |
| `/hubs/trip-tracking/**` (SignalR, WebSocket) | Trip & Tracking |
| `/api/v1/notifications/**`, `/announcements/**`, `/push-subscriptions/**`, `/hubs/notifications/**` | Notification (solo con `NotificationProxy:Enabled=true`; si no, 503 `SERVICE_DISABLED`) |

**No se exponen:** `/internal/**`, `/dev/**`, Swagger/OpenAPI de los servicios, `/health` de los servicios y la
creación de organizaciones (`POST /api/v1/organizations` con `X-Provisioning-Key`, que es solo de la plataforma).
Responden 404 `GATEWAY_ROUTE_NOT_FOUND`.

## Seguridad
- La autenticación y autorización las aplica **cada servicio** con la seguridad de Iam. El Gateway reenvía el token de
  Iam tal cual (`Authorization: Bearer …`, y `?access_token=` para SignalR).
- Límite de intentos de **inicio de sesión y registro**: `Gateway:SignInRateLimit` (10 por minuto e IP), 429 `TOO_MANY_REQUESTS`.
- Tamaño máximo del cuerpo: `Gateway:MaxRequestBodyBytes` (2 MB).
- `Gateway:TrustForwardedHeaders=true` solo detrás de un ingress de confianza (si no, la IP se puede falsear).
- CORS: `Cors:AllowedOrigins` (la web de la empresa; las apps móviles no usan CORS).

## Errores propios del Gateway (ProblemDetails con `code`)
| HTTP | `code` | Cuándo |
|---|---|---|
| 404 | `GATEWAY_ROUTE_NOT_FOUND` | Ruta no pública |
| 429 | `TOO_MANY_REQUESTS` | Demasiados intentos de sign-in / sign-up |
| 502 | `UPSTREAM_UNAVAILABLE` | El servicio no responde (caído o reiniciando) |
| 503 | `SERVICE_DISABLED` | Servicio deshabilitado en el entorno (Notification) |
| 504 | `UPSTREAM_TIMEOUT` | El servicio tardó demasiado |

Los errores de los servicios (401 `TOKEN_EXPIRED`, 422 `TRIP_HAS_PENDING_STUDENTS`, …) llegan sin cambios.
Cada respuesta trae `X-Correlation-Id` (se crea si la app no lo envía) para rastrear un error en los logs.

## Salud
- `/health/live`: el proceso responde.
- `/health/ready`: 200 solo si responden el monolito (`/health`) y Trip & Tracking (`/health/live`) — `Gateway:Readiness`.
  Si `NotificationProxy:Enabled=true`, tambien consulta Notification (`/health/ready`): requiere funciones habilitadas,
  tablas/migraciones propias disponibles y consumidor suscrito cuando RabbitMQ esta habilitado. Su liveness no sustituye readiness.

## Ejecutar
```powershell
./scripts/Initialize-NotificationLocal.ps1
docker compose --env-file .local/notification/compose.env up -d --build  # Compose principal, incluye Notifications
dotnet run --project src/ApiGateway              # local: http://localhost:8000 → monolito :5000 y Trip & Tracking :5180
dotnet test tests/RutaSegura.ApiGateway.Tests    # enrutamiento + punta a punta con Iam y Trip & Tracking reales
```
Gateway queda en http://localhost:8000 (emulador Android: http://10.0.2.2:8000). Los comandos Docker se ejecutan desde la raiz del repositorio; `--env-file` carga los secretos locales generados, sin usar un segundo Compose de Notifications.
Las direcciones de los servicios se cambian con `ReverseProxy:Clusters:<cluster>:Destinations:primary:Address`
(en docker-compose: `ReverseProxy__Clusters__monolith__Destinations__primary__Address`).
