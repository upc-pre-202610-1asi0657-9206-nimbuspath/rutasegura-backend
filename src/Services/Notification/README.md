# Notification Service — fases 1 y 2

Core de notificaciones de RutaSegura en .NET 10: Domain y Application con dependencias hacia el nucleo. Se reutilizan AggregateRoot, Result, Dispatcher, validadores y los decoradores Logging → Validation → Transaction de BuildingBlocks.

## Organizacion DDD + Clean Architecture

Es un microservicio independiente, no un modulo del monolito IAM. Tiene cinco proyectos y un solo desplegable: Domain, Application, Infrastructure, Interfaces (biblioteca REST) y Api (host). Se adopta la convencion de carpetas de IAM sin referenciar Monolith ni los ensamblados de otros servicios. Program.cs y la configuracion general permanecen en RutaSegura.Notification.Api como composition root; Interfaces contiene exclusivamente Rest.

```text
RutaSegura.Notification.Domain/
  Model/
    Aggregates/            # Notification, Announcement
    Entities/              # NotificationDelivery
    ValueObjects/          # Contenido, clave de idempotencia, filtros y estados
  Repositories/            # INotificationRepository, IAnnouncementRepository
RutaSegura.Notification.Application/
  CommandServices/         # INotificationCommandService
  QueryServices/           # INotificationQueryService
  Model/{Commands,Queries,Results}/
  Internal/
    CommandServices/       # NotificationCommandService y handlers internos
    QueryServices/         # NotificationQueryService y handlers internos
    OutboundServices/      # Puertos hacia directorios, identidad y canales
    Authorization/
    Policies/
    Validators/
    Transform/             # Entidad -> resultado de aplicacion
  Contracts/V1/
  DependencyInjection/
RutaSegura.Notification.Infrastructure/
  DependencyInjection/
  Persistence/EntityFrameworkCore/  # Contexto propio, configuraciones, repositorios y migraciones MySQL
  Security/                        # Cifrado de tokens push
RutaSegura.Notification.Interfaces/
  Rest/
    Controllers/           # Ubicacion reservada; sin endpoints de negocio en fase 1
    Resources/             # Contratos REST; no exponen entidades
    Transform/             # Assemblers Resource -> Command y Result -> Resource
RutaSegura.Notification.Api/
  Program.cs
  appsettings.json
  Properties/launchSettings.json
```

Los servicios publicos exponen Handle(command/query, cancellationToken). Sus implementaciones delegan al Dispatcher compartido para preservar validacion, logging y una sola confirmacion de la unidad de trabajo; los handlers con la logica de cada caso quedan bajo Internal/CommandServices o Internal/QueryServices. No se duplica la logica en los servicios ni se confirma manualmente la transaccion.

Commands y Queries permanecen en Application/Model: implementan los contratos CQRS de BuildingBlocks.Application. Ponerlos en Domain como estan hoy introduciria una dependencia de Domain hacia Application. Los Resources son exclusivos de Interfaces; los Results de Application son respuestas de los casos de uso independientes de HTTP. Interfaces transforma esos resultados mediante assemblers y solo referencia Application, no Domain ni Infrastructure. Api referencia Application, Infrastructure e Interfaces y concentra arranque, registro de dependencias, OpenAPI y configuracion de ejecucion. Cuando se implementen los controllers REST, Api registrara su assembly para que ASP.NET Core los descubra; no se agregan endpoints de negocio en esta fase.

La fase 2 implementa NotificationDbContext propio, no el AppDbContext del monolito. Las tablas se configuran mediante Infrastructure/Persistence/EntityFrameworkCore/Configuration/Extensions/ModelBuilderExtensions.ApplyNotificationConfiguration, llamado desde OnModelCreating. Los repositorios concretos estan en Persistence/EntityFrameworkCore/Repositories, sin mezclar reglas de dominio con EF. La migracion inicial incluye seis tablas, indices unicos, control de concurrencia y relaciones internas protegidas por organizacion. Configuracion, secretos, provisionamiento y pruebas en el [README de persistencia](src/RutaSegura.Notification.Infrastructure/Persistence/EntityFrameworkCore/README.md).

## Implementado

- Avisos por alumno y destinatario: abordaje, desembarque, proximidad, incidencia, SOS y entrega al finalizar el viaje. El SOS conserva prioridad critica y no requiere GPS.
- Comunicados de administrador para su organizacion o una ruta, con requestId idempotente. Roster valida el alcance de la ruta y resuelve los destinatarios.
- Bandeja propia paginada, detalle y marcado de lectura idempotente. La identidad proviene de INotificationUserContext, no de parametros elegidos por el cliente. Para leer avisos de alumnos se consultan los vinculos actuales; no se usa tutorOf como unica autorizacion.
- NotificationDelivery separa aceptacion del canal, reintento, fallo y omision de ReadAt. Un destino push es una suscripcion identificada; aceptar un mensaje no acredita su visualizacion ni su lectura.
- Puertos para persistencia, trabajo de entrega pendiente, Roster, estado activo del viaje, identidad, FCM y SignalR. No hay adaptadores ficticios en produccion.
- Pruebas de dominio, casos de uso mediante el Dispatcher real, contratos, dependencias y arranque del host.
- Persistencia MySQL 8.4 propia para avisos, comunicados, suscripciones push cifradas, Inbox de eventos procesados, trabajos pendientes y entregas por canal. Repositorios y unidad de trabajo reales, migraciones y Compose exclusivo de la base.
- Idempotencia del evento completo: huella del payload y sus metadatos; confirmacion atomica de Inbox + avisos + trabajo pendiente. Restricciones de base de datos para duplicados concurrentes, incluidos avisos sin alumno, y contador de version para actualizaciones concurrentes.

## Contratos con Trip & Tracking

Los seis contratos de entrada estan en Application/Contracts/V1. Infrastructure debera deserializar el payload concreto y traducir IntegrationMessage<T> a NotificationEventMetadata + ReceiveTripEventCommand; Application no referencia RabbitMQ.Client ni BuildingBlocks.Messaging.

Exchange propuesto: `trip-tracking.events`; cola propia: `notification.trip-events.v1`. MessageType/routing keys: `trip.student-boarded.v1`, `trip.student-alighted.v1`, `trip.vehicle-approaching-stop.v1`, `trip.incident-reported.v1`, `trip.sos-raised.v1`, `trip.completed.v1`.

El productor debe acordar estos nombres y agregar OrganizationId desde el propietario real del viaje. No se infiere de un identificador recibido sin verificar ni se cambia de identidad al reintentar: MessageId debe conservar el identificador del hecho original. OccurredAt representa el instante del hecho, no el de sincronizacion; CorrelationId se conserva en cada aviso. El payload evita nombres, direcciones, tokens y coordenadas GPS.

Correspondencias: StudentBoarded/StudentAlighted → StudentId; VehicleApproachingStop → alumnos de la parada y ETA; IncidentReported → alumnos afectados y NotifyGuardians; SosRaised → alumnos afectados y operadores autorizados. TripCompletedV1 requiere Direction y SchoolDeliveredStudentIds: solo las entregas nuevas confirmadas al cerrar un recojo. No debe copiarse sin filtrar el resumen DeliveredStudents del evento de dominio actual: el productor debe enriquecer el contrato con la direccion del viaje y las entregas de esa transicion. El cierre de un retorno no genera otra confirmacion de entrega porque StudentAlighted ya la comunico. No se avisa entrega de alumnos ausentes. Llegar por GPS a una parada no confirma por si solo la entrega de un alumno.

El listado actual de contratos no transporta movimiento continuo. Tracking sigue siendo propietario de GPS, geocercas, ETA, historial y Redis. El canal efimero `trip-tracking.live-positions` y su distribucion por SignalR se integraran aparte de estos avisos durables.

## Persistencia y limites para las fases siguientes

1. **MySQL 8.4 propio (implementado):** NotificationDbContext, migraciones y seis tablas. Inbox + avisos + trabajo pendiente se confirman mediante un unico SaveChanges transaccional. La integracion RabbitMQ queda para la fase de mensajeria: ACK solo despues de un commit confirmado, nunca al capturar una excepcion de unicidad.
2. **Unicidad (implementada):** NotificationKey = OrganizationId + SourceMessageId + SourceMessageType + RecipientUserId + SubjectId. SubjectId usa Guid.Empty para avisos sin alumno, con un CHECK de consistencia. Announcement tiene unicidad OrganizationId + RequestId. El Inbox tiene identidad consumer + messageId y una huella para detectar payloads conflictivos. Los indices protegen reintentos concurrentes; ante una carrera se descarta el scope y se reevalua el mensaje en uno nuevo. La comprobacion del core por si sola no sustituye estas restricciones.
3. **Entrega fuera de la transaccion:** INotificationDispatchQueue solo persiste trabajo; un worker posterior expande destinos y llama a FCM/SignalR. Conservar intentos, proximo reintento y errores estables; suscripciones push invalidas/deshabilitadas no eliminan la bandeja. Revalidar permisos, expiracion y vigencia del viaje antes del envio cuando corresponda. No prometer push exactamente una vez: hay una ventana entre la aceptacion externa y el guardado local; el cliente deduplica por notificationId.
4. **Directorios seguros:** Roster es propietario de vinculos y pertenencia al transportista. Resolver destinatarios y enlaces vigentes mediante contratos protegidos o proyecciones con politica explicita de revocacion. Si no puede verificarse el permiso, el adaptador falla; no debe devolver datos viejos como si fueran autorizacion vigente. Trip & Tracking provee IsActive sin compartir tablas ni Redis con el dominio de Notification.
5. **JWT, Gateway y clientes:** mapear sub/NameIdentifier, role/scopes y organizationId emitidos por IAM a NotificationUser. Habilitar /api/v1/notifications/** y el hub por YARP; no exponer broker ni base. Web consume bandeja, SignalR y registra push FCM; el movil del conductor sigue enviando acciones a Tracking.
6. **Redis:** no es persistencia de avisos. Solo incorporar un backplane si se despliegan varias replicas de SignalR. No reemplaza Inbox ni el trabajo durable de MySQL. Esta fase incorpora Compose solo para la base propia y configuracion externa de secretos; despliegue de todos los servicios, readiness, reintentos/DLQ y observabilidad siguen pendientes.

NotificationProcessingPolicy permite configurar caducidad de proximidad y tolerancia de reloj; los valores iniciales del core son 5 minutos y 30 segundos respectivamente. Son decisiones iniciales de implementacion ajustables, no mediciones ni valores exigidos por el reporte. Los avisos historicos de abordaje y entrega conservan el instante real y no se descartan como una proximidad efimera.

## Registro y verificacion

AddNotificationApplication() registra CommandServices, QueryServices y los handlers de los casos de uso. La fase 2 aporta repositorios e IUnitOfWork reales cuando se configura ConnectionStrings:NotificationDatabase y una clave de tokens externa. Api conserva registerUseCases: false hasta disponer de directorios e identidad productivos; solo expone liveness y OpenAPI. No migra la base automaticamente. Las pruebas registran dobles de directorios exclusivamente en sus proyectos y prueban la persistencia contra MySQL real.

Con el SDK 10.0.401 fijado por el repositorio:

```powershell
dotnet test src/Services/Notification/tests/RutaSegura.Notification.Tests/RutaSegura.Notification.Tests.csproj -c Release
```

La suite RutaSegura.Notification.Persistence.Tests verifica migraciones, persistencia, rollback, indices, cifrado, aislamiento y carreras contra MySQL 8.4 real mediante scripts/Test-NotificationPersistence.ps1. Sin NOTIFICATION_TEST_MYSQL, los casos de integracion se omiten explicitamente. Estas pruebas no acreditan RabbitMQ, Firebase ni clientes web, ni validan toda la solucion global.

Verificacion focalizada del 22/10/2026 tras fase 2: 109 pruebas aprobadas en Release, sin fallos ni omisiones: 78 de dominio/aplicacion/contratos/host y 31 de persistencia, modelo, cifrado y registro. De estas ultimas, 20 se ejecutan contra MySQL 8.4 real y verifican migraciones, atomicidad, unicidad, aislamiento y concurrencia; las otras 11 no requieren un servidor. Los contenedores temporales de prueba se retiraron al terminar, sin tocar bases o volumenes existentes. La base permanente de Compose queda preparada para configurarla con secretos propios; no se han almacenado credenciales reales en el repositorio. Se verifico con el SDK 10.0.300 instalado, ejecutando los proyectos por ruta absoluta desde E:\. La verificacion con el SDK fijado 10.0.401 y la solucion completa sigue pendiente hasta disponer de ese SDK y reconciliar las referencias IAM de los proyectos globales de pruebas. No se modifico global.json ni se ejecutaron comandos Git.
