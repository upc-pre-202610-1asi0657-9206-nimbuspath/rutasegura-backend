# RutaSegura – Backend
 
Plataforma de transporte escolar seguro. Participantes: el conductor ejecuta el viaje y registra abordajes, el tutor ve el viaje en vivo y recibe avisos, y el administrador del transportista gestiona rutas, alumnos y flota.
Proyecto académico UPC 1ASI0657 (equipo NimbusPath). La documentación de arquitectura está en el repo `rutasegura-architecture` (caps. IV y V).
 
## Stack (restricción C-01, no cambiar sin ADR)
- .NET 10 / C# / ASP.NET Core. El SDK está fijado en `global.json`.
- MySQL 8.4 con EF Core. Cada servicio tiene su propio esquema (Database per Service).
- ActiveMQ con Apache NMS (Amazon MQ en producción).
- Redis para la caché de la última posición GPS (TTL 30 s).
- YARP como API Gateway, con el rate limiter "telemetry".
- JWT Bearer, OpenAPI/Swagger, Docker y Docker Compose.
- Despliegue en AWS sa-east-1: ECS Fargate, RDS, Amazon MQ, ElastiCache, Secrets Manager y CloudWatch.
- **No usar MediatR** por su licencia. Se usa el `IDispatcher` propio de BuildingBlocks.
## Servicios (ADD Iteración 1)
| Servicio | Bounded contexts | Esquema MySQL |
|---|---|---|
| Identity & Access Service | Identity & Access | `identity` |
| Trip & Tracking Service | Trip Execution + Tracking | `trip` |
| Notification Service | Notification | `notification` |
| Administration Service | Route Planning + Roster + Subscription (3 módulos) | `route_planning`, `roster`, `subscription` |
| Gateway | YARP | – |
 
Los servicios se comunican de forma asíncrona mediante domain events. Cada contexto publica en el tópico `<context>.events`. Ningún servicio accede a la base de datos de otro.
 
## Estructura de la solución
```
src/
  BuildingBlocks/            # RutaSegura.BuildingBlocks.{Domain,Application,Messaging,Testing}
  Services/
    TripTracking/
      RutaSegura.TripTracking.Domain/          # sin dependencias externas
      RutaSegura.TripTracking.Application/     # depende solo de Domain + BuildingBlocks.Application
      RutaSegura.TripTracking.Infrastructure/  # EF Core, ActiveMQ, Redis, ACL OpenRouteService
      RutaSegura.TripTracking.Api/             # controllers, DI, Program.cs
    Identity/ Notification/ Administration/    # misma plantilla hexagonal
  Gateway/RutaSegura.Gateway/
tests/
  <Servicio>.Domain.Tests / .Application.Tests / .Integration.Tests / .Acceptance.Tests
  Architecture.Tests         # NetArchTest: reglas de dependencia
docker-compose.yml
```
Para empezar, BuildingBlocks vive en el mismo repo y se referencia con `ProjectReference`. Se extraerá al repo `rutasegura-building-blocks` (GitHub Packages) cuando llegue a la versión 1.0.0.
 
## Reglas de diseño (obligatorias)
- Arquitectura hexagonal: Domain no referencia Application, Infrastructure ni Api. Application no referencia Infrastructure. Lo verifica `Architecture.Tests`.
- Agregados que heredan de `AggregateRoot`, con IDs fuertemente tipados (`TripId`, `ClientEventId`) y value objects como `GeoPosition`.
- Las violaciones de reglas de negocio lanzan `DomainException(code)`. `DomainExceptionHandler` (IExceptionHandler) las convierte en 422 con ProblemDetails (RFC 9457) y pone el `code` en `extensions`.
- Los handlers **no hacen commit**. Lo hace `TransactionCommandDecorator`, con la cadena Logging → Validation → Transaction.
- Los controllers solo usan `IDispatcher`. No se inyectan handlers con `[FromServices]`.
- Los eventos se publican con el patrón Outbox (`OutboxMessage` + interceptor + `OutboxPublisher<TDbContext>`) y se consumen con `IdempotentEventHandler<T>`.
- Para la hora se usa siempre `TimeProvider`, nunca `DateTime.UtcNow`.
- Las llamadas a proveedores externos pasan por un ACL con `AddStandardResilienceHandler`.
- Cada servicio expone health checks en `/health/live` y `/health/ready`.
## Lenguaje de dominio (respetar exactamente)
- `TripStatus`: Scheduled, InProgress, Delayed, Completed, Cancelled.
- Códigos de error: `TRIP_INVALID_TRANSITION`, `STUDENT_NOT_IN_ROSTER`, `TRIP_HAS_PENDING_STUDENTS`, `TRACKING_REQUIRES_ACTIVE_TRIP`, `TRIP_ACCESS_DENIED`, `REFRESH_TOKEN_REUSED`, `VEHICLE_CAPACITY_EXCEEDED`, `PLAN_ROUTE_LIMIT_REACHED`.
- Solo se aceptan posiciones GPS si hay un viaje activo (C-12).
- `RecordBoarding` es idempotente por `clientEventId`. La sincronización offline ordena los eventos por `occurredAt`.
- Endpoints ya definidos:
  - `POST /api/v1/trips/{id}/boardings`
  - `POST /api/v1/trips/{id}/complete`
  - `GET /api/v1/trips/{id}/live` (tutor)
  - `POST /api/v1/trips/{id}/sync`
## Convenciones
- El código, los tests y los archivos `.feature` se escriben en inglés. Los comentarios de negocio pueden ir en español.
- Rutas REST en kebab-case y versionadas como `/api/v1`. El JSON usa camelCase y los enums se serializan en UPPER_SNAKE.
- MySQL: tablas y columnas en snake_case, con índices `ix_` y restricciones únicas `uq_`.
- Nombres de tests con el formato `Method_Scenario_ExpectedResult`, estructura AAA y Traits por categoría.
- Testing: xUnit v3, Shouldly, NSubstitute, FakeTimeProvider, Reqnroll (Gherkin), WebApplicationFactory y Testcontainers (MySQL y ActiveMQ).
- Cobertura mínima: Domain ≥ 80 % y Application ≥ 70 %.
## Git
- GitFlow con las ramas `main`, `develop`, `feature/<us-id>-desc`, `refactor/<id>-desc`, `release/X.Y.Z` y `hotfix/X.Y.Z`.
- Conventional Commits con los scopes `trip`, `identity`, `notification`, `administration`, `gateway`, `api`, `messaging`, `infra`.
  - Ejemplo: `feat(trip): record student boarding`.
- Commits pequeños, uno por tarea del Sprint Backlog (T01–T33). Nunca se hace push directo a `main` ni a `develop`.
## Comandos
```
dotnet build
dotnet test
dotnet test --filter "Category=Unit"
docker compose up -d mysql activemq redis
dotnet run --project src/Services/TripTracking/RutaSegura.TripTracking.Api
```
 
## Cómo trabajar en este repo (para Claude)
- Antes de escribir código de una historia, propone un plan corto y espera la aprobación.
- Escribe primero las pruebas de dominio y después la implementación. Ejecuta `dotnet build` y `dotnet test` antes de dar algo por terminado.
- No agregues paquetes NuGet fuera del stack sin preguntar.
- Si algo contradice este archivo, detente y pregunta en lugar de improvisar.
 
