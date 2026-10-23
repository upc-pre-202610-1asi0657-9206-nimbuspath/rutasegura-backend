# Trip & Tracking Service

Microservicio del bounded context **Trip & Tracking** (fusión de Trip Execution + Tracking, ADR de la It. 1).
Recibe el GPS del celular del conductor, gestiona el ciclo de vida de cada viaje (abordajes, ausencias,
incidentes, SOS, cierre), publica sus eventos en RabbitMQ y difunde el movimiento del bus en tiempo real.

## Arquitectura hexagonal

```
src/
  RutaSegura.TripTracking.Domain          ← núcleo: agregado Trip, tracking GPS, políticas. Sin dependencias.
  RutaSegura.TripTracking.Application     ← casos de uso (CQRS) + PUERTOS (Abstractions/Ports.cs)
                                            + contratos publicados (IntegrationEvents) y réplicas (Replicas)
  RutaSegura.TripTracking.Infrastructure  ← adaptadores de salida: EF Core/MySQL, Outbox/Inbox, RabbitMQ, Redis, workers
  RutaSegura.TripTracking.Api             ← adaptador de entrada: REST /api/v1 + SignalR /hubs/trip-tracking
tests/
  RutaSegura.TripTracking.Domain.Tests       ← unitarias del core (89)
  RutaSegura.TripTracking.Application.Tests  ← casos de uso con fakes de cada puerto (72)
  RutaSegura.TripTracking.Integration.Tests  ← API + MySQL + RabbitMQ reales (17; se omiten si no hay infraestructura)
  Features/                                  ← escenarios Gherkin US-10 a US-14
```

Regla de dependencias: `Api → Infrastructure → Application → Domain`. La API no referencia el dominio
(recibe enums como texto vía `TripInputs`); el dominio y la aplicación no conocen MySQL, RabbitMQ, Redis ni ASP.NET.

| Puerto (Application) | Adaptador (Infrastructure) |
|---|---|
| `ITripRepository` | `TripRepository` (EF Core, agregado completo con split query) |
| `ITrackPointRepository` | `TrackPointRepository` → tabla append-only `track_points` |
| `IUnitOfWork` (BuildingBlocks) | `EfUnitOfWork`: agregado + telemetría + **Outbox** en una transacción, concurrencia optimista por agregado (`version`) |
| `ILivePositionPublisher` | `LivePositionPublisher`: después del commit → Redis + difusión en vivo |
| `ILastPositionCache` | `DistributedLastPositionCache` → Redis `trip:{id}:position` (memoria si no hay Redis) |
| `IRouteCatalog` / `IRouteCatalogStore` | `RouteCatalog` → réplica `route_snapshots` |
| `IGuardianDirectory` / `IGuardianDirectoryStore` | `GuardianDirectory` → réplica `guardian_students` |
| `IMessagePublisher` (BuildingBlocks) | `RabbitMqMessagePublisher` (publisher confirms) |

## Plan por etapas

| Etapa | Contenido | Estado |
|---|---|---|
| 1 | Estructura hexagonal, agregado `Trip`, tracking GPS, `GuardianAccessPolicy` + pruebas | ✅ |
| 2 | Casos de uso, puertos, Dispatcher y decoradores + pruebas | ✅ |
| 3 | EF Core + MySQL 8.4 (`TripTrackingDbContext`, migración `InitialCreate`), `EfUnitOfWork`, Outbox, concurrencia optimista | ✅ |
| 4 | RabbitMQ: `OutboxProcessor` → `trip-tracking.events`, canal en vivo `trip-tracking.live`, consumidor idempotente con Inbox y DLQ, Redis, programador diario | ✅ |
| 5 | API REST + JWT de IAM + SignalR, ProblemDetails, health live/ready, OpenAPI/Swagger, Docker, pruebas de integración | ✅ |

## API REST (`/api/v1`, JWT de IAM)

| Rol | Método y ruta | Caso de uso |
|---|---|---|
| DRIVER | `GET /drivers/me/trips?from&to` | Agenda (por defecto hoy, hora de Lima) |
| DRIVER | `POST /trips/{id}/start` · `/complete` | US-10 / US-14 (`clientEventId`) |
| DRIVER | `POST /tracking/positions` `{tripId, readings[]}` | Lote GPS (≤ 500), rate limit "telemetry" |
| DRIVER | `POST /trips/{id}/boardings` · `/alightings` · `/absences` · `/passengers/{studentId}/undo` | US-11 |
| DRIVER | `POST /trips/{id}/incidents` | US-12 (`incidentType`: `HeavyTraffic` o `HEAVY_TRAFFIC`…) |
| DRIVER | `POST /trips/{id}/sos` | US-13 (nunca falla por falta de GPS) |
| DRIVER | `POST /trips/{id}/sync` `{events[]}` | Cola offline (≤ 200): Applied / Duplicate / Rejected |
| PARENT | `GET /guardians/me/trips` | Viajes de mis hijos (solo mis hijos) |
| PARENT | `POST /trips/{id}/guardian-absences` | "Hoy no va" |
| PARENT, DRIVER, ADMIN | `GET /trips/{id}/live` · `/position` | Estado en vivo / posición rápida (Redis) |
| DRIVER, ADMIN | `GET /trips/{id}/summary` · `/track` | Resumen final / recorrido GPS |
| ADMIN | `GET /trips?from&to&status` · `POST /trips` | Panel y programación de la empresa |
| ADMIN | `POST /trips/{id}/cancel` · `/sos/resolve` | Cancelar / atender SOS |

- Errores RFC 9457 con `extensions.code` estable (ej. `TRIP_HAS_PENDING_STUDENTS` 422, `TRIP_DRIVER_MISMATCH` 403,
  `TRIP_ACCESS_DENIED` 403, `CONCURRENCY_CONFLICT` 409 → reintentar con el mismo `clientEventId`).
- El admin solo ve y opera viajes de su empresa (claim `organizationId`).
- **Tiempo real:** `/hubs/trip-tracking?access_token=JWT` → `SubscribeToTrip(tripId)` (devuelve la última posición) y
  eventos `PositionUpdated` / `TripStatusChanged`. Cada réplica escucha `trip-tracking.live`, así escala sin backplane.
- `GET /health/live` (proceso) y `GET /health/ready` (MySQL + RabbitMQ; Redis como Degraded).

## Mensajería

- **Publica** (Outbox → exchange topic `trip-tracking.events`, routing key = tipo): `trip-tracking.trip-scheduled`,
  `trip-started`, `trip-delayed`, `student-boarded`, `student-alighted`, `student-marked-absent`, `passenger-record-undone`,
  `vehicle-approaching-stop`, `stop-reached`, `incident-reported`, `sos-raised`, `sos-resolved`, `trip-completed`,
  `trip-cancelled` (contratos `*V1` en `Application/IntegrationEvents`). `MessageId` = `EventId` para idempotencia.
  Si aún no hay consumidores, los mensajes quedan en `trip-tracking.events.unrouted` (alternate exchange).
- **Consume** (cola `trip-tracking.inbound`, DLQ `trip-tracking.inbound.dlq`): `*.route-published|route-updated|route-retired`
  y `*.guardian-student-linked|unlinked` (contratos en `Infrastructure/Messaging/Inbound/InboundContracts.cs`).

## Ejecutar

```bash
# Todo con dependencias reales (MySQL 8.4, RabbitMQ, Redis), migraciones y datos demo "Ruta 3"
docker compose -f src/Services/Trip-Tracking/docker-compose.yml up --build
# → http://localhost:5180/swagger · POST /dev/tokens {"role":"DRIVER"} · RutaSegura.TripTracking.Api.http

# Solo la API (necesita MySQL local; RabbitMQ y Redis son opcionales)
dotnet run --project src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api -- --migrate
```

Configuración: `ConnectionStrings:TripTracking` (MySQL), `ConnectionStrings:Redis` (opcional), `Jwt:Secret`
(**el mismo** `TokenSettings:Secret` de IAM), `RabbitMq:*`, `Outbox:*`, `Scheduling:*`, `Seed:Demo`, `DevTokens:Enabled`
(solo Development). Nueva migración: `dotnet ef migrations add <Nombre> -p src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Infrastructure -o Persistence/Migrations`.

## Pruebas

```bash
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Domain.Tests
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Application.Tests
# Integración: MySQL en localhost:3306 (usuario rutasegura) o TRIPTRACKING_IT_MYSQL / TRIPTRACKING_IT_RABBITMQ
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Integration.Tests
```

## Reglas de negocio cubiertas (core)

- Solo el conductor asignado opera el viaje (`TRIP_DRIVER_MISMATCH`).
- Inicio desde 60 min antes; >10 min tarde ⇒ `Delayed`.
- GPS solo con viaje activo (`TRACKING_REQUIRES_ACTIVE_TRIP`). Lecturas descartadas: precisión >100 m,
  saltos >150 km/h, duplicadas/viejas, futuras, anteriores al inicio, coordenada (0,0).
- Detección de parada: aviso "el bus se acerca" a 800 m (una vez por parada) y llegada a 60 m.
- Idempotencia por `clientEventId` en todas las acciones del conductor (reintentos y modo offline).
- Sincronización offline ordenada por hora real del evento, con resultado por evento (Applied/Duplicate/Rejected).
- SOS nunca falla por falta de GPS; no se puede finalizar con SOS activo.
- No se finaliza con alumnos pendientes; en retorno, tampoco con alumnos a bordo.
- Privacidad (AC-01): el tutor solo ve a sus hijos, solo la parada de sus hijos y la ubicación solo mientras el viaje está en curso.
