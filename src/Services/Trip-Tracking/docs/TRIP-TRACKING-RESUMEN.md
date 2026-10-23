# Trip & Tracking — Resumen de lo implementado

> Bounded context **Trip & Tracking** de RutaSegura (fusión de *Trip Execution* + *Tracking*, ADR de la It. 1).
> Responsable: Hurtado Balcazar, Rommel Daniel · Actualizado: 23/10/2026 · .NET 10 · MySQL 8.4 · RabbitMQ · Redis.
> Contratos para otros servicios: [`TRIP-TRACKING-CONTRATOS.md`](TRIP-TRACKING-CONTRATOS.md).

## 1. Qué hace el bounded context

Recibe el GPS del celular del conductor, gestiona el ciclo de vida de cada viaje escolar (inicio, abordajes, ausencias,
incidentes, SOS, cierre), publica lo ocurrido como eventos de integración y difunde la posición del bus en tiempo real
a tutores y empresa. Es dueño de su propia base de datos (Database per Service); de otros contextos solo guarda réplicas.

| Historia | Descripción | Dónde se implementa |
|---|---|---|
| US-10 | Iniciar el viaje asignado y transmitir GPS | `Trip.Start`, `Trip.RecordPosition`, `StartTripCommand`, `RecordPositionsCommand` |
| US-11 | Registrar abordaje y desembarque | `RecordBoarding/Alighting`, `MarkStudentAbsent`, `UndoPassengerRecord` |
| US-12 | Reportar incidencias | `Trip.ReportIncident`, `ReportIncidentCommand` |
| US-13 | Activar y atender SOS | `Trip.RaiseSos/ResolveSos`, `RaiseSosCommand`, `ResolveSosCommand` |
| US-14 | Finalizar el viaje y generar su resumen | `Trip.Complete`, `CompleteTripCommand`, `GetTripSummaryQuery` |
| TS-15 | Última posición en Redis y canal en tiempo real | `LivePositionPublisher`, `DistributedLastPositionCache`, hub SignalR |
| TS-16 | Eventos confiables con RabbitMQ, Outbox e idempotencia | `EfUnitOfWork`, `OutboxProcessor`, `InboundEventsConsumer` |

## 2. Arquitectura hexagonal (4 capas)

```
src/Services/Trip-Tracking/
├─ src/
│  ├─ RutaSegura.TripTracking.Domain          ← núcleo: agregado Trip, GPS, políticas. Sin dependencias externas.
│  ├─ RutaSegura.TripTracking.Application     ← casos de uso CQRS, puertos, contratos publicados, réplicas
│  ├─ RutaSegura.TripTracking.Infrastructure  ← EF Core/MySQL, Outbox/Inbox, RabbitMQ, Redis, workers
│  └─ RutaSegura.TripTracking.Api             ← REST /api/v1, SignalR, JWT, ProblemDetails, health
├─ tests/  (Domain.Tests · Application.Tests · Integration.Tests · Features/*.feature)
├─ docs/   (este archivo y los contratos)
├─ Dockerfile · docker-compose.yml · README.md
```

Regla de dependencias: `Api → Infrastructure → Application → Domain`. Verificada con `ArchitectureRules`
(Mono.Cecil) del equipo sobre los 4 ensamblados compilados: **sin violaciones**. La API no referencia el dominio:
recibe enums como texto y los traduce `TripInputs` (Application).

### 2.1 Domain (Etapa 1)
- **Agregado `Trip`**: `Schedule`, `Start`, `RecordPosition`, `RecordBoarding`, `RecordAlighting`, `MarkStudentAbsent`,
  `NotifyGuardianAbsence`, `UndoPassengerRecord`, `ReportIncident`, `RaiseSos`, `ResolveSos`, `Complete`, `Cancel`.
- Entidades internas: `TripStop`, `TripPassenger`, `Incident`, `SosAlert`. Value objects: `GeoPosition` (Haversine),
  `GpsReading`, `TrackPoint`, IDs fuertemente tipados (`TripId`, `StudentId`, `OrganizationId`, `ClientEventId`…).
- Políticas: `TrackingPolicy` (umbrales GPS), `EtaEstimator`, `GuardianAccessPolicy` (AC-01).
- 14 domain events: `TripScheduled`, `TripStarted`, `TripDelayed`, `StudentBoarded`, `StudentAlighted`,
  `StudentMarkedAbsent`, `PassengerRecordUndone`, `VehicleApproachingStop`, `StopReached`, `IncidentReported`,
  `SosRaised`, `SosResolved`, `TripCompleted`, `TripCancelled`.
- **Nuevo en esta entrega:** `OrganizationId` (empresa de transporte) en el viaje y `Trip.BelongsTo(org)`.

**Reglas de negocio**

| Regla | Código |
|---|---|
| Solo el conductor asignado opera el viaje | `TRIP_DRIVER_MISMATCH` |
| Inicio desde 60 min antes; >10 min tarde ⇒ estado `Delayed` | `TRIP_START_TOO_EARLY` |
| GPS solo con viaje en curso | `TRACKING_REQUIRES_ACTIVE_TRIP` |
| Lecturas ignoradas (no fallan): precisión >100 m, >150 km/h, repetidas/viejas, futuras, antes del inicio | — |
| Coordenada (0,0) o fuera de rango | `TRACKING_INVALID_COORDINATES` |
| Aviso "el bus se acerca" a 800 m (una vez) y llegada a 60 m | — |
| Idempotencia por `clientEventId` en todas las acciones del conductor | — |
| No finalizar con alumnos pendientes / con alumnos a bordo en retorno / con SOS activo | `TRIP_HAS_PENDING_STUDENTS`, `TRIP_HAS_STUDENTS_ON_BOARD`, `TRIP_HAS_ACTIVE_SOS` |
| Incidente "Other" exige detalle; máximo 500 caracteres | `INCIDENT_DETAIL_REQUIRED`, `INCIDENT_DETAIL_TOO_LONG` |
| SOS nunca falla por falta de GPS | — |
| Tutor: solo sus hijos, solo la parada de sus hijos, ubicación solo con viaje en curso | `TRIP_ACCESS_DENIED` |
| Admin: solo viajes de su empresa | `TRIP_ACCESS_DENIED` |

### 2.2 Application (Etapa 2 + ampliaciones)
- Dispatcher propio (BuildingBlocks) con decoradores **Logging → Validation → Transaction**; los handlers nunca confirman.
- **Comandos (16):** `ScheduleTrip`, `StartTrip`, `RecordPositions`, `RecordBoarding`, `RecordAlighting`,
  `MarkStudentAbsent`, `UndoPassengerRecord`, `NotifyAbsence`, `ReportIncident`, `RaiseSos`, `ResolveSos`,
  `CompleteTrip`, `CancelTrip`, `SyncOfflineEvents` + réplicas `UpsertRouteSnapshot`, `RemoveRouteSnapshot`,
  `LinkGuardianStudent`, `UnlinkGuardianStudent`.
- **Queries (7):** `GetTripLiveStatus`, `GetTripSummary`, `GetDriverAgenda` y, nuevas, `GetGuardianTrips`,
  `GetOrganizationTrips`, `GetTripTrack`, `GetTripPosition`.
- **Puertos:** `ITripRepository`, `ITrackPointRepository`, `ILivePositionPublisher`, `ILastPositionCache`,
  `IGuardianDirectory`/`IGuardianDirectoryStore`, `IRouteCatalog`/`IRouteCatalogStore`.
- **Contratos publicados** (`IntegrationEvents/TripIntegrationEvents.cs`): 14 eventos `*V1` + `TripIntegrationEventMapper`.

### 2.3 Infrastructure (Etapas 3 y 4)

| Componente | Responsabilidad |
|---|---|
| `TripTrackingDbContext` + migración `InitialCreate` | 10 tablas en snake_case: `trips`, `trip_stops`, `trip_passengers`, `trip_incidents`, `trip_sos_alerts`, `track_points`, `outbox_messages`, `inbox_messages`, `route_snapshots`, `guardian_students` |
| Mapeo EF | Agregado sin modificar el dominio: colecciones privadas por backing field, `GeoPosition` como complex type opcional, IDs y fechas (UTC `datetime(6)`) por convención, `clientEventId` procesados como JSON |
| `EfUnitOfWork` | Una transacción: agregado + `track_points` + **Outbox**. Concurrencia optimista por agregado (`version`) → 409 `CONCURRENCY_CONFLICT`; índice único (ruta, hora) → 409 `DUPLICATE_RECORD`. Acciones "after commit" (Redis, canal en vivo) |
| `OutboxProcessor` | Publica en RabbitMQ con *publisher confirms*, `FOR UPDATE SKIP LOCKED` (varias réplicas), backoff exponencial, limpieza a 7 días |
| `RabbitMqMessagePublisher` | Exchange topic `trip-tracking.events` con *alternate exchange* `trip-tracking.events.unrouted` (nada se pierde sin consumidores) |
| `InboundEventsConsumer` | Cola `trip-tracking.inbound` + DLQ, Inbox idempotente, ACK tras commit, un reintento y luego DLQ |
| `LivePositionPublisher` + `DistributedLastPositionCache` | Redis `trip:{id}:position` (TTL 30 min) + difusión por `trip-tracking.live` |
| `LiveUpdatesRelay` | Cada réplica escucha `trip-tracking.live` y empuja a SignalR (escala sin backplane) |
| `TripSchedulingWorker` | Programa automáticamente los viajes de hoy y mañana desde la réplica de rutas (idempotente) |
| `DemoDataSeeder` | Datos del prototipo (Ruta 3, Jorge, Valeria, Mateo, Camila) con `Seed:Demo=true` |
| Health checks | MySQL y RabbitMQ (Unhealthy), Redis (Degraded: el servicio sigue con MySQL) |

### 2.4 Api (Etapa 5)
- REST `/api/v1` con controllers delgados (`TripsController`, `TrackingController`, `MeController`) que solo usan `IDispatcher`.
- JWT de IAM (HMAC-SHA256, mismo secreto que `TokenSettings:Secret`), políticas por rol `DRIVER`, `PARENT`, `ADMIN`.
- Errores RFC 9457 (ProblemDetails) con `code` estable; `DomainException` → 422, conflicto → 409.
- Hub SignalR `/hubs/trip-tracking`, rate limit "telemetry" (120 lotes/min por conductor).
- `/health/live`, `/health/ready`, OpenAPI + Swagger UI con esquema Bearer, `/dev/tokens` solo en Development.
- `Dockerfile` multi-stage y `docker-compose.yml` (MySQL 8.4, RabbitMQ 3.13, Redis 7, API con migraciones y datos demo).

## 3. Pruebas

**178 pruebas del servicio, todas en verde** (más 16 de BuildingBlocks). Herramientas: xUnit v3, Shouldly,
FakeTimeProvider, WebApplicationFactory, MySQL y RabbitMQ reales. Patrones: AAA, Test Data Builder (`TripBuilder`),
Object Mother ("Ruta 3"), Test Doubles (fakes de cada puerto), Disposable Test Environment (BD por clase de prueba).

| Proyecto | Pruebas | Qué cubre |
|---|---|---|
| `Domain.Tests` | **89** | Agregado y value objects, sin infraestructura |
| `Application.Tests` | **72** | Casos de uso con fakes de los puertos |
| `Integration.Tests` | **17** | API + MySQL + RabbitMQ reales (se **omiten**, no fallan, si no hay infraestructura) |

### 3.1 Dominio (89)
| Clase | # | Cubre |
|---|---|---|
| `TripSchedulingTests`, `TripStartTests`, `TripCompletionTests` | 21 | Definición válida del viaje, ventana de inicio, retraso, cierre de recojo/retorno, pendientes, SOS activo |
| `TripPassengerTests` | 17 | Abordaje, bajada, "No vino", aviso del tutor, deshacer, alumno ajeno, idempotencia |
| `TripTrackingTests` | 14 | Filtros GPS (precisión, saltos, duplicados, futuro), detección de parada y aviso de cercanía |
| `TripIncidentTests`, `TripSosTests` | 11 | Incidentes que retrasan, detalle obligatorio/largo, SOS sin GPS, un solo SOS activo, resolución |
| `GeoPositionTests`, `GpsReadingTests`, `EtaEstimatorTests` | 8 | Rangos de coordenadas, (0,0), Haversine, ETA |
| `GuardianAccessPolicyTests` | 4 | Privacidad del tutor (AC-01) |
| `TripOrganizationTests` | 2 | Viaje acotado a su empresa |

### 3.2 Aplicación (72)
| Clase | # | Cubre |
|---|---|---|
| `ScheduleTripCommandHandlerTests`, `StartTripCommandHandlerTests`, `CompleteAndCancelTripCommandHandlerTests` | 11 | Programación idempotente, ruta inexistente o que no opera, inicio, cierre, cancelación |
| `RecordPositionsCommandHandlerTests` | 7 | Lote GPS: guarda telemetría y publica una sola actualización en vivo; nada aceptado ⇒ nada publicado |
| `SyncOfflineEventsCommandHandlerTests` | 6 | Orden por hora real, duplicados, evento inválido no bloquea al resto |
| `PassengerCommandHandlerTests`, `SafetyCommandHandlerTests` | 8 | Abordajes, deshacer, aviso del tutor solo por sus hijos, incidentes, SOS |
| `GetTripLiveStatusQueryHandlerTests`, `TripSummaryAndAgendaQueryHandlerTests` | 7 | Vista del tutor/conductor, posición oculta al finalizar, resumen y agenda |
| `OrganizationScopingTests` | 8 | Admin de otra empresa denegado, token sin empresa denegado, tutor solo ve su parada, programar copia la empresa |
| `MonitoringQueryTests` | 11 | Viajes del tutor, panel de empresa, recorrido GPS, posición desde Redis o MySQL, rangos de fecha |
| `TripInputsTests` | 4 (+casos) | `HeavyTraffic`/`HEAVY_TRAFFIC`, tipos y estados desconocidos |
| `TripIntegrationEventMapperTests` | 2 | Viaje completo produce los 13 contratos en orden, `MessageId = EventId`, empresa nula sin organización |
| `ReplicaCommandTests` | 3 | Validación de rutas (zona horaria, paradas) y vínculo tutor-alumno |

### 3.3 Integración (17)
| Clase | Pruebas |
|---|---|
| `TripApiTests` (8) | Viaje completo por HTTP persistido con `track_points` y 8 eventos en Outbox · `clientEventId` repetido no duplica · 422 con `TRIP_HAS_PENDING_STUDENTS` · seguridad 401/403 (rol, conductor ajeno, tutor ajeno, admin de otra empresa) · tutor ve solo a su hijo y la posición solo con viaje en curso · sync offline · admin programa, lista y cancela · `/health/ready` |
| `PersistenceAndMessagingTests` (6) | Dos cambios simultáneos ⇒ `CONCURRENCY_CONFLICT` · índice único ⇒ `DUPLICATE_RECORD` · ida y vuelta del agregado conserva posiciones, SOS sin GPS y `clientEventId` · Outbox publica una sola vez · consumidor de entrada: procesado/duplicado/inválido→DLQ/ignorado · programador diario idempotente |
| `RabbitMqTests` (3) | Publicación con confirmación y sobre completo · evento sin suscriptores queda en `.unrouted` · posición viaja por el broker hasta el sink en vivo |

### 3.4 Verificación manual
Recorrido completo con la API en ejecución: tokens → agenda → inicio → GPS → abordajes → incidente → SOS → resolución →
cierre → resumen; Outbox publicado en RabbitMQ; consumo de rutas y vínculos desde `administration.events`/`iam.events`
(duplicado ignorado, inválido a DLQ); Redis poblado y limpiado al cerrar; cliente SignalR recibe `PositionUpdated`.

### 3.5 Cómo ejecutarlas
```powershell
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Domain.Tests
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Application.Tests
docker compose -f src/Services/Trip-Tracking/docker-compose.yml up -d mysql rabbitmq redis
$env:TRIPTRACKING_IT_MYSQL = "server=localhost;port=3307;user=root;password=root"
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Integration.Tests
```

## 4. Ejecutar y probar la API
```powershell
docker compose -f src/Services/Trip-Tracking/docker-compose.yml up -d --build
```
- Swagger: http://localhost:5180/swagger → `POST /dev/tokens` con `{ "role": "DRIVER" }` (o `PARENT`, `ADMIN`) → **Authorize**.
- RabbitMQ: http://localhost:15672 (guest/guest) · MySQL: `localhost:3307` (rutasegura/rutasegura).
- `RutaSegura.TripTracking.Api.http` contiene el recorrido completo encadenado.
- Un viaje solo se inicia desde 60 min antes de su hora (06:40 y 13:30 en la demo); antes responde `TRIP_START_TOO_EARLY`.

## 5. Pendientes y decisiones abiertas
- IAM debe firmar con el mismo secreto (`Jwt:Secret` = `TokenSettings:Secret`).
- Administration e IAM deben publicar los eventos de entrada acordados (ver contratos).
- Step definitions Reqnroll para los `.feature` US-10 a US-14 (los escenarios ya están cubiertos por las pruebas).
- `Architecture.Tests` y `Backend.Structure.Tests` referencian `src/Services/IAM`, que hoy vive en `src/Monolith`.
