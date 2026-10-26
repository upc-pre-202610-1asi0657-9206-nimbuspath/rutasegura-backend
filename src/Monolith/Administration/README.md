# Administration — RutaSegura Monolith

Bounded Context **Administration** del monolito (reemplaza al microservicio `src/Services/Administration`, que se eliminó). Agrupa dos módulos:

- **Roster**: conductores y vehículos de la operación (US-01, US-08).
- **Route Planning**: rutas, paradas (dónde se recoge o se deja a cada niño), alumnos por parada, asignación de conductor + vehículo (US-04, US-05) y el límite de rutas activas del plan contratado.

Además reacciona a los eventos que publica **Trip & Tracking** (incidentes, SOS, viajes completados) para el seguimiento administrativo.

> Estado: las cuatro capas (**Domain**, **Application**, **Infrastructure**, **Interfaces**) están implementadas, registradas en `Program.cs` (`AddAdministrationBoundedContext`) y probadas. La seguridad la provee **Iam** (ver *Seguridad*).

```text
Administration/
  Domain/
    Model/
      Aggregates/    Driver, Vehicle, Route, OperationalAlert, TripRecord (+ *Audit parciales)
      Entities/      RouteStop
      ValueObjects/  ids, DocumentNumber, DriverLicense, LicensePlate, SeatingCapacity, RouteSchedule, GeoLocation, PlanLimits, …
      Commands/      RosterCommands, RoutePlanningCommands, OperationsMonitoringCommands
      Queries/       AdministrationQueries
      Events/        RouteCreated/Assigned/Activated/DetailsChanged/Deactivated
      Exceptions/    AdministrationDomainException (lleva un AdministrationError)
      AdministrationError.cs
    Repositories/    IDriverRepository, IVehicleRepository, IRouteRepository, IOperationalAlertRepository, ITripRecordRepository
    Services/        RouteScheduleConflictPolicy
  Application/
    CommandServices/ IRosterCommandService, IRoutePlanningCommandService, IOperationsMonitoringCommandService
    QueryServices/   IRosterQueryService, IRoutePlanningQueryService, IOperationsMonitoringQueryService, ActiveRouteQuota
    IntegrationEvents/
      Inbound/       contratos de Trip & Tracking (IncidentReportedV1, SosRaisedV1, SosResolvedV1, TripCompletedV1)
      Outbound/      contratos publicados (RoutePublishedV1, RouteRetiredV1, RouteAssignedV1) + RouteIntegrationEventMapper
    Internal/
      CommandServices/, QueryServices/   implementaciones
      OutboundServices/                  puertos: IPlanLimitsProvider (Subscription), IIntegrationEventOutbox, ACL/IIamAccountService
  Infrastructure/
    Persistence/EFC/  ModelBuilderExtensions (ApplyAdministrationConfiguration), conversores y repositorios
    Messaging/        Outbox (+ OutboxPublisherService), Inbound (InboundMessageProcessor, TripTrackingEventsConsumer), RabbitMq
    Subscription/     ConfiguredPlanLimitsProvider (plan por configuración mientras no exista Subscription)
    Acl/              IamAccountService → IIamContextFacade (fachada publicada por Iam)
    Configuration/    AddAdministrationBoundedContext
  Interfaces/Rest/    DriversController, DriverProfileController, VehiclesController, RoutesController,
                      OperationalAlertsController, Resources/, Transform/ (assemblers y AdministrationError → HTTP)
```

## Persistencia (MySQL, migración `AddAdministrationBoundedContext`)

| Tabla | Contenido |
| :--- | :--- |
| `roster_drivers`, `roster_vehicles` | Agregados Driver y Vehicle (nombre, licencia y modelo como *complex types*) |
| `route_itineraries` | Agregado Route: horario y asignación (opcional) como *complex types*; `enrollment_code` único (migración `AddStakeholderBoundedContext`) |
| `route_stops` | Paradas (FK `route_id`, cascada); alumnos de cada parada en `student_ids` (JSON); `origin` = `MANUAL` (ADMIN) o `ENROLLMENT` (creada por una inscripción) |
| `operations_alerts`, `operations_trip_records` | Incidentes/SOS y viajes completados recibidos de Trip & Tracking |
| `administration_outbox_messages`, `administration_inbox_messages` | Outbox (publicación confiable) e Inbox (consumo idempotente) |

Las fechas se guardan en UTC (`datetime(6)`). Todos los índices únicos de las reglas (documento y placa por organización, cuenta IAM por conductor, nombre de ruta por organización, incidente, viaje) existen también en la base.

## Endpoints

Todos requieren el JWT de Iam. La organización **siempre** sale del claim `organizationId` del token, nunca del body. Los errores son RFC 7807 con `code` (= `AdministrationError`).

| Política | Método y ruta | Uso |
| :--- | :--- | :--- |
| ADMIN | `POST/GET /api/v1/roster/drivers`, `GET /{driverId}`, `GET /{driverId}/routes` | Registrar y consultar conductores |
| ADMIN | `POST /api/v1/roster/drivers/{id}/verify` · `/suspend` · `/reinstate`, `PUT /{id}/contact` · `/license` | Ciclo de vida del conductor |
| DRIVER | `GET /api/v1/roster/drivers/me`, `GET /me/routes`, `GET /me/vehicles` | El conductor autenticado ve su perfil, sus rutas (sin código de inscripción) y los vehículos de sus rutas (placa, modelo, asientos) |
| ADMIN | `POST/GET /api/v1/roster/vehicles`, `GET /{id}`, `PUT /{id}/capacity`, `POST /{id}/enable` · `/disable` | Flota y aforo |
| ADMIN | `POST/GET /api/v1/routes`, `GET /quota`, `GET/DELETE /{routeId}`, `PUT /{routeId}/name` · `/schedule` | Rutas y cupo del plan |
| ADMIN | `POST /api/v1/routes/{id}/stops`, `PUT/DELETE /{id}/stops/{stopId}`, `PUT /{id}/stops/order` | Paradas (dónde se recoge / deja a cada niño) |
| ADMIN | `PUT /api/v1/routes/{id}/stops/{stopId}/students/{studentId}`, `DELETE /{id}/students/{studentId}` | Mover un alumno de parada o quitarlo (los padres inscriben con el código, ver Stakeholder) |
| ADMIN | `POST /api/v1/routes/{id}/enrollment-code` | Nuevo código de inscripción para las familias (el anterior deja de funcionar) |
| ADMIN | `PUT/DELETE /api/v1/routes/{id}/assignment`, `POST /{id}/activate` · `/deactivate`, `GET /{id}/trip-records` | Asignación, activación (límite del plan) e historial |
| ADMIN | `GET /api/v1/operations/alerts?onlyOpen=true`, `POST /{alertId}/acknowledge` · `/resolve` | Seguimiento de incidentes y SOS |

Estados HTTP: 404 no encontrado (también para recursos de otra organización), 409 conflicto con el estado (duplicados, transiciones, horario solapado, aforo, `PlanRouteLimitReached`), 422 cuenta/licencia/organización inválida, 400 datos inválidos.

## Seguridad (provista por Iam)

- `AddIamSecurity` (Iam) valida el JWT que emite Iam y define las políticas `IamPolicies.OrganizationAdministrator` (rol ADMIN + `organizationId`) y `IamPolicies.OrganizationDriver` (rol DRIVER + `organizationId`).
- En cada request, `ActiveIamAccountHandler` consulta a Iam mediante su fachada `IIamContextFacade`. Comprueba que la cuenta del token siga existiendo, tenga el mismo rol y la misma organización, y que esa organización esté activa. Así un token vigente pierde el acceso si la cuenta se borra, cambia de rol o se suspende la empresa.
- 401 (sin token, firma inválida, vencido) y 403 (rol u organización no permitidos, cuenta ya no válida) responden en formato ProblemDetails.
- Administration solo usa de Iam `Monolith.Iam.Interfaces.ACL` (fachada, roles) y `Monolith.Iam.Interfaces.Security` (políticas, claims); una prueba de arquitectura lo verifica.

## Configuración (`appsettings.json`)

```json
"Database": { "MigrateOnStartup": true },
"Administration": {
  "Plans": { "Default": { "PlanCode": "BASIC", "MaxActiveRoutes": 3 }, "Organizations": { "<organizationId>": { "PlanCode": "PRO", "MaxActiveRoutes": 10 } } },
  "Messaging": { "Enabled": false, "HostName": "localhost", "EventsExchange": "administration.events", "InboundQueue": "administration.inbound" }
}
```

Con `Messaging:Enabled = false` los eventos se acumulan en el outbox. Con `true` se activan `OutboxPublisherService` (publisher confirms; `FOR UPDATE SKIP LOCKED` en MySQL) y `TripTrackingEventsConsumer`, que crea la cola `administration.inbound` con su DLQ y la enlaza a `trip-tracking.events`.

## Reglas de negocio

| Regla | Dónde | Error |
| :--- | :--- | :--- |
| Conductor nace `PENDING_VERIFICATION`; solo `VERIFIED` con licencia vigente se asigna | `Driver` | `DriverNotAssignable`, `DriverLicenseExpired` |
| El conductor se vincula a una cuenta IAM con rol DRIVER de la misma organización | `RosterCommandService` + `IIamAccountService` | `DriverAccountNotValid`, `DriverAccountAlreadyLinked` |
| Documento único por organización / placa única por organización (`abc-123` = `ABC123`) | `RosterCommandService` | `DriverAlreadyRegistered`, `VehiclePlateAlreadyRegistered` |
| No suspender un conductor ni deshabilitar un vehículo que opera una ruta activa | `RosterCommandService` | `DriverHasActiveRoutes`, `VehicleHasActiveRoutes` |
| Paradas con secuencia 1..n, máx. 30, separadas ≥ 60 m (radio de llegada de Trip & Tracking) | `Route` | `RouteStopTooClose`, `RouteStopLimitReached` |
| Un alumno está en una sola parada de la ruta; no se borra una parada con alumnos | `Route` | `RouteStopHasStudents` |
| Aforo: alumnos de la ruta ≤ asientos del vehículo asignado | `Route`, `Vehicle` | `VehicleCapacityExceeded` |
| Conductor y vehículo deben ser de la misma organización y estar habilitados | `Route.AssignResources` | `ResourceBelongsToAnotherOrganization`, `VehicleNotAssignable` |
| Un conductor/vehículo no puede estar en dos rutas con horario solapado (mismo día y ventana) | `RouteScheduleConflictPolicy` | `ResourceScheduleConflict` (409) |
| Ruta activa: ≥ 1 parada y asignación; activar respeta el **límite de rutas activas del plan** | `Route.Activate` + `IPlanLimitsProvider` | `RouteNotReadyForActivation`, `PlanRouteLimitReached` (409) |
| Una ruta activa no se borra, no cambia de sentido ni pierde su asignación | `Route` | `RouteInvalidTransition` |
| Un SOS se resuelve en Trip & Tracking; aquí solo se atiende (acknowledge) | `OperationalAlert` | `AlertInvalidTransition` |
| Inscripción de familias: el niño va a la parada a ≤ 150 m de su recojo o a una parada `ENROLLMENT` nueva (inserción más barata); una parada `ENROLLMENT` vacía se elimina | `Route.PlaceStudent` / `RemoveStudent` | `RouteNotAcceptingEnrollments` (INACTIVE), `VehicleCapacityExceeded`, `RouteStopLimitReached` |

Toda consulta y comando lleva el `OrganizationId` del administrador autenticado (claim del JWT); los recursos de otra organización se responden como *no encontrados*.

**Límite del plan.** Subscription aún no existe: Route Planning lo consulta por el puerto `IPlanLimitsProvider`. La infraestructura dará un adaptador con un plan por configuración y, cuando exista Subscription, llamará a su fachada sin cambiar Domain ni Application. Solo las rutas `ACTIVE` cuentan; desactivar una libera cupo.

## Fachada para Stakeholder (ACL)

`Interfaces/ACL/IRoutePlanningContextFacade` es lo único que Stakeholder usa de Route Planning: buscar la ruta por código, leer rutas y paradas de los alumnos, las rutas de un conductor, y colocar o quitar a un niño (`PlaceStudentOnRouteCommand` / `RemoveStudentFromRouteCommand`, que pasan por las mismas reglas y el mismo outbox que el resto de casos de uso).

## Integración con Trip & Tracking (RabbitMQ)

Los nombres del informe se mapean a los tipos de mensaje que **realmente** usa el código de Trip & Tracking:

| Informe | Dirección | Tipo de mensaje (routing key) | Contrato |
| :--- | :--- | :--- | :--- |
| `RouteAssignedEvent` | Administration → | `administration.route-assigned` | `RouteAssignedV1` |
| (ruta lista para operar) | Administration → Trip & Tracking | `administration.route-published` | `RoutePublishedV1` |
| (cambio de ruta activa) | Administration → Trip & Tracking | `administration.route-updated` | `RoutePublishedV1` |
| (ruta deja de operar) | Administration → Trip & Tracking | `administration.route-retired` | `RouteRetiredV1` |
| `IncidentReportedEvent` | Trip & Tracking → Administration | `trip-tracking.incident-reported` | `IncidentReportedV1` |
| `SosAlertRaisedEvent` | Trip & Tracking → Administration | `trip-tracking.sos-raised` | `SosRaisedV1` |
| (SOS atendido) | Trip & Tracking → Administration | `trip-tracking.sos-resolved` | `SosResolvedV1` |
| `TripCompletedEvent` | Trip & Tracking → Administration | `trip-tracking.trip-completed` | `TripCompletedV1` |

Trip & Tracking **no escucha** `route-assigned`: para programar viajes necesita la ruta completa (paradas, alumnos, horario, conductor y vehículo), por eso consume `route-published/updated/retired` (exchange `administration.events`). `RouteAssignedEvent` se conserva como evento de dominio y se publica para otros consumidores (p. ej. Notification).

`driverId` en los contratos es el **id de usuario IAM** del conductor: Trip & Tracking lo compara con el JWT de la app móvil (`TRIP_DRIVER_MISMATCH`).

Qué hace Administration con lo que recibe: incidente → `OperationalAlert` con severidad por tipo (Accident=CRITICAL, VehicleFailure/StudentUnwell=HIGH, RouteDeviation/Other=MEDIUM, HeavyTraffic=LOW); SOS → alerta CRITICAL abierta hasta `sos-resolved`; viaje completado → `TripRecord` (asistencia, duración, incidentes) para reportes. Todo es idempotente (id de incidente, un SOS abierto por viaje, un registro por viaje).

## Pruebas

```powershell
dotnet test tests/RutaSegura.Monolith.Administration.Tests
dotnet test tests/RutaSegura.Monolith.Administration.ContractTests
dotnet test tests/RutaSegura.Monolith.Administration.IntegrationTests
```

- `RutaSegura.Monolith.Administration.Tests`: dominio (value objects, agregados, política de conflictos), aplicación con dobles en memoria de los puertos (Roster, Route Planning, eventos de Trip & Tracking, outbox) y reglas de capas DDD leídas del IL con Mono.Cecil.
- `RutaSegura.Monolith.Administration.IntegrationTests` (34): host real del monolito con SQLite en memoria y tokens reales de Iam. Cubre el flujo completo por HTTP, la persistencia del agregado Route, el Outbox, el Inbox con eventos de Trip & Tracking, OpenAPI y la seguridad: 401, 403, aislamiento por organización, token manipulado o vencido, cuenta borrada, rol cambiado y organización suspendida.
- `RutaSegura.Monolith.Administration.ContractTests`: serializa lo que publica Administration y lo lee con los tipos **reales** de Trip & Tracking (pasa su validador `UpsertRouteSnapshotCommand`), y viceversa con los eventos `*V1` de Trip & Tracking.
