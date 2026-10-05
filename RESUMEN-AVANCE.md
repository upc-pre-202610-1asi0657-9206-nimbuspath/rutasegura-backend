# RutaSegura – Resumen de avance del backend

> Revisión documental 2026-10-04: RabbitMQ es el broker vigente. El catálogo de historias y la selección del Sprint 1 se encuentran en las secciones 3.2, 3.4 y 5.3.1 del informe AV2 de la raíz. El core existente no acredita endpoints, persistencia ni mensajería implementados. Administration conserva Roster, Route Planning y Subscription; suscripción, interfaces completas y otras capacidades posteriores no se incorporan automáticamente al Sprint 1 por existir código de dominio.

> Última actualización: 2026-10-03 · Responsable: Rommel Hurtado Balcazar
> Stack (C-01): ASP.NET Core / .NET 10 (C#), MySQL 8.4, RabbitMQ, Docker.

## 1. Qué hemos hecho

### 1.1 Estructura de la solución (`RutaSegura.slnx`)

```
src/
  ApiGateway/                          ← YARP (plantilla inicial)
  BuildingBlocks/                      ← librería compartida (5.1.3)
    RutaSegura.BuildingBlocks.Domain
    RutaSegura.BuildingBlocks.Application
    tests/RutaSegura.BuildingBlocks.Application.Tests
  Services/
    IAM/                               ← plantilla inicial (pendiente)
    Trip-Tracking/                     ← microservicio en construcción
      src/RutaSegura.TripTracking.Domain
      src/RutaSegura.TripTracking.Application
      src/RutaSegura.TripTracking.Api
      tests/RutaSegura.TripTracking.Domain.Tests
      tests/RutaSegura.TripTracking.Application.Tests
```

Arquitectura hexagonal: `Api → Infrastructure → Application → Domain`.
El dominio y la aplicación no conocen MySQL, RabbitMQ ni ASP.NET.

### 1.2 BuildingBlocks (código técnico compartido por los 4 microservicios)

| Paquete | Contenido |
|---|---|
| `.Domain` | `AggregateRoot`, `Entity`, `DomainEvent`, `DomainException` (con `Code` estable) |
| `.Application` | `Result`/`Error`, `ICommand`/`IQuery`, `Dispatcher` propio (sin MediatR), decoradores **Logging → Validation → Transaction**, `IUnitOfWork`, `IValidator`, `AddRutaSeguraApplication()` |

### 1.3 Trip-Tracking – Etapa 1: Dominio (core)

- **Agregado `Trip`**: programar, iniciar, registrar GPS, abordaje/bajada, "No vino", "Deshacer", aviso de inasistencia del apoderado, incidentes, SOS, finalizar y cancelar.
- **Tracking GPS**: `GeoPosition` (Haversine), `GpsReading`, `TrackPoint`, `TrackingPolicy` (umbrales), `EtaEstimator`.
- **`GuardianAccessPolicy`**: privacidad de menores (AC-01).
- **14 domain events**: `TripScheduled`, `TripStarted`, `TripDelayed`, `StudentBoarded`, `StudentAlighted`, `StudentMarkedAbsent`, `PassengerRecordUndone`, `VehicleApproachingStop`, `StopReached`, `IncidentReported`, `SosRaised`, `SosResolved`, `TripCompleted`, `TripCancelled`.

Reglas de negocio principales:

| Regla | Código de error |
|---|---|
| Solo el conductor asignado opera el viaje | `TRIP_DRIVER_MISMATCH` |
| Inicio desde 60 min antes; >10 min tarde ⇒ `Delayed` | `TRIP_START_TOO_EARLY` |
| GPS solo con viaje en curso | `TRACKING_REQUIRES_ACTIVE_TRIP` |
| Se descartan lecturas: precisión >100 m, saltos >150 km/h, repetidas/viejas, futuras, (0,0) | — (se ignoran, no fallan) |
| Aviso "el bus se acerca" a 800 m (una vez) y llegada a 60 m | — |
| Acciones idempotentes por `clientEventId` | — |
| No finalizar con alumnos pendientes / a bordo (retorno) / SOS activo | `TRIP_HAS_PENDING_STUDENTS`, `TRIP_HAS_STUDENTS_ON_BOARD`, `TRIP_HAS_ACTIVE_SOS` |
| SOS nunca falla por falta de GPS | — |
| Tutor solo ve a sus hijos y la ubicación solo con viaje activo | `TRIP_ACCESS_DENIED` |

### 1.4 Trip-Tracking – Etapa 2: Aplicación

- **Puertos de salida**: `ITripRepository`, `ITrackPointRepository`, `ILivePositionPublisher` (tiempo real), `IGuardianDirectory`, `IRouteCatalog`.
- **Comandos**: `ScheduleTrip`, `StartTrip`, `RecordPositions` (lotes ≤500), `RecordBoarding`, `RecordAlighting`, `MarkStudentAbsent`, `UndoPassengerRecord`, `NotifyAbsence`, `ReportIncident`, `RaiseSos`, `ResolveSos`, `CompleteTrip`, `CancelTrip`, `SyncOfflineEvents` (≤200).
- **Queries**: `GetTripLiveStatus`, `GetTripSummary`, `GetDriverAgenda`.

### 1.5 Pruebas

**133 pruebas**: 87 de dominio, 40 de aplicación y 6 de BuildingBlocks.
Herramientas: xUnit v3, Shouldly y FakeTimeProvider. Patrones: AAA, Test Data Builder (`TripBuilder`), Object Mother ("Ruta 3") y Test Doubles (fakes de cada puerto).

```powershell
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Domain.Tests
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Application.Tests
dotnet test src/BuildingBlocks/tests/RutaSegura.BuildingBlocks.Application.Tests
```

## 2. Resumen de tests – 2 por funcionalidad core

| # | Funcionalidad | Test | Qué verifica |
|---|---|---|---|
| 1 | **Ciclo de vida del viaje** | `TripStartTests.Start_MoreThanTenMinutesLate_StartsDelayedAndRaisesTripDelayed` | Iniciar 15 min tarde deja el viaje en `Delayed` y emite `TripDelayed`. |
| | | `TripCompletionTests.Complete_PickupWithEveryoneRegistered_DeliversBoardedStudentsAndRaisesTripCompleted` | Al finalizar un recojo, los alumnos a bordo quedan entregados; `TripCompleted` informa 2 entregados, 1 ausente y 1 incidente. |
| 2 | **Filtrado de GPS** | `TripTrackingTests.RecordPosition_ImpossibleJump_IsIgnoredAsOutlier` | Un salto de 4.4 km en 30 s (~530 km/h) se descarta y la última posición no cambia. |
| | | `TripTrackingTests.RecordPosition_ScheduledTrip_ThrowsTrackingRequiresActiveTrip` | No se acepta ubicación si el viaje no está en curso (C-12). |
| 3 | **Detección de paradas** | `TripTrackingTests.RecordPosition_EnteringApproachRadius_RaisesVehicleApproachingStopOnce` | A ~556 m de la parada se emite **un solo** aviso, con ETA de 2 min y los alumnos que esperan allí. |
| | | `TripTrackingTests.RecordPosition_InsideArrivalRadius_MarksStopReachedAndAdvancesNextStop` | A 33 m la parada queda alcanzada y la siguiente pasa a ser la parada 2. |
| 4 | **Registro de pasajeros** | `TripPassengerTests.RecordBoarding_StudentNotInRoster_ThrowsStudentNotInRoster` | No se puede subir a un alumno que no pertenece al viaje. |
| | | `TripPassengerTests.RecordBoarding_StudentWhoseGuardianNotifiedAbsence_CanStillBoard` | Si el apoderado avisó inasistencia pero el alumno llegó, el conductor igual puede registrarlo. |
| 5 | **Idempotencia (reintentos)** | `TripPassengerTests.RecordBoarding_SameClientEventId_IsIdempotent` | El mismo `clientEventId` enviado dos veces no duplica el abordaje ni el evento. |
| | | `TripPassengerTests.RecordBoarding_RejectedAction_DoesNotConsumeClientEventId` | Si una acción falla, su `clientEventId` no se "gasta" y el reintento corregido se aplica. |
| 6 | **Sincronización offline** | `SyncOfflineEventsCommandHandlerTests.Handle_EventsArriveOutOfOrder_AppliesThemByOccurredAt` | Eventos enviados desordenados (bajada antes que subida) se aplican por hora real; la respuesta conserva el orden de envío. |
| | | `SyncOfflineEventsCommandHandlerTests.Handle_InvalidEvent_IsRejectedWithCodeAndDoesNotBlockTheRest` | Un evento inválido se informa como `Rejected` con su código y no bloquea a los demás. |
| 7 | **Incidentes** | `TripIncidentTests.ReportIncident_HeavyTraffic_RecordsIncidentAndDelaysTrip` | "Tráfico intenso" registra el incidente, pasa el viaje a `Delayed` y marca el aviso a padres. |
| | | `TripIncidentTests.ReportIncident_OtherWithoutDetail_ThrowsDetailRequired` | El tipo "Otro" exige una descripción. |
| 8 | **SOS** | `TripSosTests.RaiseSos_WithoutAnyGpsFix_StillRaisesSos` | El SOS se dispara aunque no haya ninguna posición GPS. |
| | | `TripCompletionTests.Complete_WithActiveSos_ThrowsHasActiveSos` | No se puede finalizar el viaje con un SOS activo. |
| 9 | **Privacidad del tutor (AC-01)** | `GetTripLiveStatusQueryHandlerTests.Handle_LinkedGuardian_SeesBusPositionAndOnlyOwnChild` | El tutor ve el bus, la ETA y **solo a su hijo**, nunca a otros alumnos. |
| | | `GetTripLiveStatusQueryHandlerTests.Handle_CompletedTrip_HidesPosition` | Con el viaje finalizado, ya no se expone la ubicación. |
| 10 | **Publicación en tiempo real** | `RecordPositionsCommandHandlerTests.Handle_ValidReadings_PersistsTrackPointsAndPublishesLatestPositionOnce` | Un lote de lecturas guarda el recorrido y publica **una** actualización con la última posición, la próxima parada y la ETA. |
| | | `RecordPositionsCommandHandlerTests.Handle_NothingAccepted_DoesNotPublishNorPersist` | Si todas las lecturas se descartan, no se guarda ni se publica nada. |
| 11 | **Pipeline de casos de uso (BuildingBlocks)** | `DispatcherPipelineTests.SendAsync_ValidCommand_RunsThroughAllDecoratorsAndCommits` | Un comando pasa por Logging → Validation → Transaction y se confirma una vez. |
| | | `DecoratorTests.TransactionDecorator_FailedCommand_DoesNotCommit` | Si el caso de uso falla, no se guarda nada en la BD. |

## 3. Próximas etapas

| Etapa | Contenido |
|---|---|
| 3 | Infrastructure: EF Core + MySQL 8.4, `TripDbContext` como `IUnitOfWork`, tabla `track_points`, Outbox y puertos/adaptadores de última posición Redis |
| 4 | RabbitMQ: eventos en `trip-tracking.events`, posiciones en vivo en `trip-tracking.live-positions`, consumidores idempotentes (rutas, vínculos tutor-alumno) |
| 5 | API REST + JWT + hub en tiempo real, ProblemDetails, docker-compose y pruebas de integración |

## 4. Pendientes y notas

- La carpeta `src/Services/Trip-Tracking/_to_delete/` contiene la plantilla vieja del API: se puede borrar.
- BuildingBlocks vive dentro de la solución; el plan de 5.1.3 (paquete NuGet aparte) queda para cuando esté estable.
- Por decidir antes de la Etapa 4: cómo reciben los padres el movimiento en tiempo real (hub SignalR alimentado por RabbitMQ, que es lo recomendado, o conexión directa al broker).
