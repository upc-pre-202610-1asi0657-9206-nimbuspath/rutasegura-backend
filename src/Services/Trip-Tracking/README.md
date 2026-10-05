# Trip & Tracking Service

> Revisión documental 2026-10-04: RabbitMQ es el broker vigente. El catálogo de historias y la selección del Sprint 1 se encuentran en las secciones 3.2, 3.4 y 5.3.1 del informe AV2 de la raíz. El core existente no acredita endpoints, persistencia ni mensajería implementados. Administration conserva Roster, Route Planning y Subscription; suscripción, interfaces completas y otras capacidades posteriores no se incorporan automáticamente al Sprint 1 por existir código de dominio.

Microservicio del bounded context **Trip & Tracking** (fusión de Trip Execution + Tracking, ADR de la It. 1).
Recibe el GPS del celular del conductor, gestiona el ciclo de vida de cada viaje (abordajes, ausencias,
incidentes, SOS, cierre) y difunde el movimiento del bus en tiempo real.

## Arquitectura hexagonal

```
src/
  RutaSegura.TripTracking.Domain          ← núcleo: agregado Trip, tracking GPS, políticas. Sin dependencias.
  RutaSegura.TripTracking.Application     ← casos de uso (CQRS) + PUERTOS de salida (Abstractions/Ports.cs)
  RutaSegura.TripTracking.Infrastructure  ← (Etapa 3-4) adaptadores: EF Core/MySQL, Outbox, RabbitMQ
  RutaSegura.TripTracking.Api             ← (Etapa 5) adaptador de entrada: REST + tiempo real
tests/
  RutaSegura.TripTracking.Domain.Tests       ← unitarias del core (xUnit v3 + Shouldly)
  RutaSegura.TripTracking.Application.Tests  ← casos de uso con fakes de cada puerto
```

Regla de dependencias: `Api → Infrastructure → Application → Domain`. El dominio y la aplicación
no conocen MySQL, RabbitMQ ni ASP.NET.

## Plan por etapas

| Etapa | Contenido | Estado |
|---|---|---|
| 1 | Estructura hexagonal, BuildingBlocks.Domain, agregado `Trip`, tracking GPS, `GuardianAccessPolicy` + pruebas | ✅ |
| 2 | BuildingBlocks.Application (Result, Dispatcher, decoradores), casos de uso, puertos + pruebas | ✅ |
| 3 | Infrastructure de persistencia: EF Core + MySQL 8.4, `TripDbContext` como `IUnitOfWork`, tabla `track_points`, Outbox | ⏳ |
| 4 | Mensajería RabbitMQ: OutboxPublisher → topic `trip-tracking.events`, `ILivePositionPublisher` → topic `trip-tracking.live-positions`, consumidores idempotentes (rutas, vínculos tutor-alumno) | ⏳ |
| 5 | API REST + JWT + hub de tiempo real alimentado por RabbitMQ, ProblemDetails, docker-compose, pruebas de integración | ⏳ |

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
- Privacidad (AC-01): el tutor solo ve a sus hijos y la ubicación solo mientras el viaje está en curso.

## Ejecutar pruebas

```bash
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Domain.Tests
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Application.Tests
dotnet test src/BuildingBlocks/tests/RutaSegura.BuildingBlocks.Application.Tests
```
