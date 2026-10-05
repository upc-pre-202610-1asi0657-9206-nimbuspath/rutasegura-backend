# RutaSegura Backend

Backend .NET 10 del proyecto refactorizado. El catálogo de historias y el Sprint Backlog se mantienen en el informe AV2 de la raíz. Este README describe el código ejecutable; `RESUMEN-AVANCE.md` y el README de Trip-Tracking conservan el avance previo.

## Sprint 1 — Etapa 1 (T01–T04)

La solución contiene cuatro servicios desplegables y un Gateway. Cada servicio tiene Domain, Application, Infrastructure y Api. Administration agrupa Roster, Route Planning y Subscription en un mismo desplegable.

```text
src/
  ApiGateway/                         # Host técnico; YARP se configura en T13
  BuildingBlocks/
    RutaSegura.BuildingBlocks.Domain/
    RutaSegura.BuildingBlocks.Application/
    RutaSegura.BuildingBlocks.Messaging/
    RutaSegura.BuildingBlocks.Testing/
    tests/
  Services/
    IAM/src/RutaSegura.IAM.{Domain,Application,Infrastructure,Api}/
    Administration/src/RutaSegura.Administration.{Domain,Application,Infrastructure,Api}/
    Trip-Tracking/src/RutaSegura.TripTracking.{Domain,Application,Infrastructure,Api}/
    Notification/src/RutaSegura.Notification.{Domain,Application,Infrastructure,Api}/
tests/
  RutaSegura.Architecture.Tests/
  RutaSegura.Backend.Structure.Tests/
scripts/
  Verify-BuildingBlocksPackages.ps1
  Test-Stage1Hosts.ps1
```

Los tipos `*DomainAssembly` y `*ApplicationAssembly` de los módulos nuevos identifican fronteras para organización y pruebas. No representan agregados o casos de uso ya implementados.

## Dependencias

- Domain solo utiliza su núcleo y BuildingBlocks.Domain.
- Application utiliza su Domain y BuildingBlocks.Application; no conoce EF Core, ASP.NET, Redis ni RabbitMQ.Client.
- Infrastructure implementará los puertos de Application y es registrada desde el composition root de Api.
- Los servicios no referencian entidades ni ensamblados de otros servicios.
- Roster, Route Planning y Subscription colaboran mediante contratos publicados de Application; no acceden directamente a los detalles internos de otro módulo.
- BuildingBlocks contiene código técnico, sin entidades de negocio. Testing se utiliza solo en proyectos de pruebas.

Las pruebas inspeccionan referencias de proyectos/paquetes y binarios compilados. También construyen fixtures con referencias prohibidas y accesos a otro módulo dentro de métodos para comprobar que el detector realmente los rechaza. La inspección de IL usa [Mono.Cecil](https://github.com/jbevain/cecil).

## Verificación reproducible

Desde la raíz, con el SDK 10.0.401 fijado en `global.json`:

```powershell
dotnet test RutaSegura.slnx -c Release --logger trx --results-directory artifacts/test-results/project-references
./scripts/Verify-BuildingBlocksPackages.ps1
./scripts/Test-Stage1Hosts.ps1 -UsePackages
```

El segundo comando crea cuatro paquetes `RutaSegura.BuildingBlocks.*.1.0.0.nupkg` en `artifacts/packages`, restaura en una caché nueva, compila toda la solución y ejecuta las suites con `UseBuildingBlockPackages=true`. Comprueba en `project.assets.json` que los cuatro servicios consumieron Domain, Application y Messaging como paquetes del feed local y que las pruebas consumieron Testing. No se publica en un feed externo.

El modo normal utiliza ProjectReference para desarrollo. El modo de paquetes utiliza salidas aisladas bajo `artifacts/package-consumers`; excluye `bin/obj` de los elementos fuente para impedir contaminación por archivos generados. Los resultados y la evidencia de consumo se conservan en `artifacts`.

El último comando arranca simultáneamente los cuatro servicios y Gateway en procesos Kestrel separados, con puertos locales dinámicos, verifica `200 Healthy` en `/health/live` y detiene únicamente los procesos que inició. Genera `artifacts/host-startup/results.json`.

## Ejecutar un host

```powershell
dotnet run --project src/Services/IAM/src/RutaSegura.IAM.Api
dotnet run --project src/Services/Administration/src/RutaSegura.Administration.Api
dotnet run --project src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api
dotnet run --project src/Services/Notification/src/RutaSegura.Notification.Api
dotnet run --project src/ApiGateway
```

Cada host expone `/health/live`. Los servicios publican OpenAPI en Development. Readiness, persistencia, autorización y comunicación real con RabbitMQ se completan en sus tareas posteriores; no se registran repositorios o unidades de trabajo ficticias en producción.

Trip & Tracking conserva el dominio, aplicación y sus 127 pruebas previas. Sus handlers todavía no se activan desde el host porque requieren adaptadores productivos y funcionalidades fuera de esta etapa. Las otras tres aplicaciones registran el dispatcher compartido y sus fronteras de módulos sin añadir endpoints de negocio.

## Alcance siguiente

T05–T08: Docker Compose, bases por servicio, RabbitMQ, Redis y CI. Después: contratos, seguridad, Gateway, mensajería confiable y las cuatro historias funcionales seleccionadas del Sprint 1.

El diagrama nombra Ocelot, mientras T13 y el proyecto usan YARP. Esta etapa conserva YARP y no configura rutas ni cambia decisiones del informe. Redis corresponde a Tracking; el dibujo completo no amplía las primeras 16 historias del sprint.

Los archivos `bin/obj` ya versionados requieren la limpieza prevista para T07; `.gitignore` previene nuevos artefactos, pero no elimina el trabajo previo del índice.

Detalle y evidencia: [docs/SPRINT1-ETAPA1.md](docs/SPRINT1-ETAPA1.md).
