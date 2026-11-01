# RutaSegura Backend

Backend .NET 10 del proyecto refactorizado. El catálogo de historias y el Sprint Backlog se mantienen en el informe AV2 de la raíz. Este README describe el código ejecutable; `RESUMEN-AVANCE.md` y el README de Trip-Tracking conservan el avance previo.

## Monolito: IAM, Administration y Stakeholder

**Stakeholder** (nuevo) es el bounded context de las familias: el padre registra a sus hijos (nombre, foto, dirección de recojo) y los inscribe con el código que comparte la empresa; Route Planning crea o reutiliza la parada de cada dirección. Ver [src/Monolith/Stakeholder/README.md](src/Monolith/Stakeholder/README.md). Pruebas en `tests/RutaSegura.Monolith.Stakeholder.Tests` y `StakeholderEndToEndTests`.

**Subscription** (nuevo) es el bounded context de los planes B2B (US-07): catálogo de planes, el contrato de cada empresa y los cambios de plan pagados por la pasarela de pagos (Mercado Pago Checkout Pro en modo prueba, y un adaptador sandbox para desarrollo y demos). Route Planning obtiene de su fachada el límite de rutas activas, que antes venía de `Administration:Plans`. Ver [src/Monolith/Subscription/README.md](src/Monolith/Subscription/README.md). Pruebas en `tests/RutaSegura.Monolith.Subscription.Tests` y `SubscriptionEndToEndTests`.

IAM y Administration ya no son microservicios: viven como bounded contexts dentro de `src/Monolith` (carpetas `Iam/` y `Administration/`, cada una con Domain, Application, Infrastructure e Interfaces). El microservicio `src/Services/Administration` se eliminó. Administration (Roster + Route Planning) tiene implementadas sus cuatro capas, con la seguridad de Iam integrada; ver [src/Monolith/Administration/README.md](src/Monolith/Administration/README.md). Sus pruebas están en `tests/RutaSegura.Monolith.Administration.Tests`, `.IntegrationTests` y `.ContractTests`.

## Sprint 1 — Etapa 1 (T01–T04)

La solución contiene cuatro servicios desplegables y un Gateway. Cada servicio tiene Domain, Application, Infrastructure y un host HTTP Api. Notification separa sus contratos y adaptadores REST en una biblioteca Interfaces referenciada por Api. Administration agrupa Roster, Route Planning y Subscription en un mismo desplegable.

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
    Notification/src/RutaSegura.Notification.{Domain,Application,Infrastructure,Interfaces,Api}/
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
- Infrastructure implementará los puertos de Application y es registrada desde el composition root de Api. En Notification, Interfaces contiene solo REST y depende de Application; Api compone Application, Infrastructure e Interfaces.
- Los servicios no referencian entidades ni ensamblados de otros servicios.
- Roster, Route Planning y Subscription colaboran mediante contratos publicados de Application; no acceden directamente a los detalles internos de otro módulo.
- BuildingBlocks contiene código técnico, sin entidades de negocio. Testing se utiliza solo en proyectos de pruebas.

Las pruebas inspeccionan referencias de proyectos/paquetes y binarios compilados. También construyen fixtures con referencias prohibidas y accesos a otro módulo dentro de métodos para comprobar que el detector realmente los rechaza. La inspección de IL usa [Mono.Cecil](https://github.com/jbevain/cecil).

## Probar todo en Docker (Monolito + Trip-Tracking + Notifications)

Requisitos: Docker Desktop encendido. El unico `docker-compose.yml` principal levanta Gateway, monolito, Trip-Tracking, Notifications, sus migraciones, MySQL propio de Notifications, MySQL compartido, RabbitMQ y Redis. Los servicios usan el mismo secreto JWT, issuer y audience. Primero se generan los secretos locales; el inicializador conserva las claves existentes y no imprime sus valores.

```powershell
./scripts/Initialize-NotificationLocal.ps1
docker compose --env-file .local/notification/compose.env up -d --build --wait
docker compose --env-file .local/notification/compose.env --profile smoke run --rm smoke
docker compose --env-file .local/notification/compose.env logs -f monolith trip-tracking notification
docker compose --env-file .local/notification/compose.env down  # apaga conservando los datos
```

| Servicio | URL |
| :--- | :--- |
| Gateway (entrada de las apps) | http://localhost:8000 |
| Monolito (Iam + Administration + Stakeholder) | http://localhost:5000/swagger |
| Trip-Tracking | http://localhost:5180/swagger |
| Notifications (readiness; OpenAPI/metricas requieren secreto) | http://localhost:5182/health/ready |
| RabbitMQ (rutasegura / secreto local generado) | http://localhost:15672 |
| MySQL (rutasegura/rutasegura, root/root) | localhost:3307 |

Notifications usa una instancia MySQL exclusiva sin puerto publicado, migraciones previas al arranque, secretos por archivos y rutas habilitadas en Gateway. No necesita otro Compose para el arranque normal. El archivo adicional de FCM es opcional. Los contratos de destinatarios del monolito y el contexto de padres sin organizacion siguen pendientes; el arranque saludable no acredita el flujo completo de avisos a familias. Instrucciones y pruebas especificas en [Notifications](src/Services/Notification/README.md#fase-5-integracion-y-cierre).

La prueba de humo (`scripts/smoke/e2e.sh`) corre en un contenedor y no necesita nada instalado. Hace este recorrido:
1. Verifica las colas y bindings de RabbitMQ.
2. **Iam:** alta de la organización con su ADMIN, registro de un padre, rechazos 401/403/400 y creación del conductor por el ADMIN.
3. **Administration:** conductor, vehículo, ruta, paradas, alumno, asignación y activación.
4. **Evento `administration.route-published` hacia Trip-Tracking:** este programa el viaje.
5. **Trip-Tracking:** el conductor (con token de Iam) inicia el viaje, reporta un incidente, lanza un SOS que el ADMIN resuelve, marca la ausencia del alumno y finaliza.
6. **Eventos de vuelta a Administration:** `incident-reported`, `sos-raised`, `sos-resolved` y `trip-completed` se convierten en alertas y en el registro del viaje.
7. Desactiva la ruta.
8. **Familias por el Gateway (Stakeholder):** el padre registra a su hija con foto y la inscribe con el código; la ruta crea la parada, el viaje ya programado la incluye, el padre ve el viaje y el conductor la ve con el teléfono del padre; luego la retira.
9. Comprueba que las colas DLQ estén vacías.

**Si un arranque falla a medias:** revisar los logs y el estado de migraciones/credenciales antes de reintentar; conservar volumenes y claves existentes. MySQL solo inicializa usuarios y bases con el volumen vacio. Borrar volumenes o regenerar la clave AES no es un procedimiento de recuperacion de datos.

**Pruebas de integración de Trip-Tracking contra este Docker:** sin estas variables se omiten.
```powershell
$env:TRIPTRACKING_IT_MYSQL="server=localhost;port=3307;user=root;password=root"
$env:TRIPTRACKING_IT_RABBITMQ="localhost"
dotnet test src/Services/Trip-Tracking/tests/RutaSegura.TripTracking.Integration.Tests
```

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
dotnet run --project src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api
dotnet run --project src/Services/Notification/src/RutaSegura.Notification.Api
dotnet run --project src/ApiGateway
```

Cada host expone `/health/live`. Los servicios publican OpenAPI en Development. Readiness, persistencia, autorización y comunicación real con RabbitMQ se completan en sus tareas posteriores; no se registran repositorios o unidades de trabajo ficticias en producción.

Notification ya implementa su fase 2 de persistencia propia: NotificationDbContext, repositorios EF Core, migraciones, Inbox, trabajos de entrega y suscripciones push cifradas. La base MySQL 8.4 se configura de forma externa y cuenta con Compose aislado y pruebas de integracion reales; ver [Notification Service](src/Services/Notification/README.md). El host no migra automaticamente ni activa todavia los casos de uso que requieren directorios e identidad productivos. Las herramientas EF 10.0.12 se restauran con `dotnet tool restore` desde el manifiesto local.

Trip & Tracking conserva el dominio, aplicación y sus 127 pruebas previas. Sus handlers todavía no se activan desde el host porque requieren adaptadores productivos y funcionalidades fuera de esta etapa. Las otras tres aplicaciones registran el dispatcher compartido y sus fronteras de módulos sin añadir endpoints de negocio.

## Alcance siguiente

T05–T08: Docker Compose, bases por servicio, RabbitMQ, Redis y CI. Después: contratos, seguridad, Gateway, mensajería confiable y las cuatro historias funcionales seleccionadas del Sprint 1.

El diagrama nombra Ocelot, mientras T13 y el proyecto usan YARP. Esta etapa conserva YARP y no configura rutas ni cambia decisiones del informe. Redis corresponde a Tracking; el dibujo completo no amplía las primeras 16 historias del sprint.

Los directorios `bin/obj`, resultados de pruebas y configuraciones locales del IDE no se versionan. Si ya estaban registrados, deben retirarse del índice con `git rm --cached`; añadirlos a `.gitignore` por sí solo no elimina su seguimiento. La limpieza conserva las copias locales.

Después de actualizar el repositorio, comprobar `dotnet --version` desde la raíz y ejecutar `dotnet restore RutaSegura.slnx`. Se requiere el SDK 10.0.401 fijado en `global.json`; tener otro SDK .NET 10 no garantiza que se pueda compilar esta solución. Los archivos de NuGet de `obj/` se regeneran localmente y no deben recuperarse de Git ni compartirse entre equipos.

Si la restauración informa proyectos inexistentes, comprobar las rutas de `RutaSegura.slnx` y los `ProjectReference`: es necesario integrar los proyectos faltantes o corregir las referencias según la estructura acordada. La restauración de NuGet no crea esos proyectos.

Detalle y evidencia: [docs/SPRINT1-ETAPA1.md](docs/SPRINT1-ETAPA1.md).
