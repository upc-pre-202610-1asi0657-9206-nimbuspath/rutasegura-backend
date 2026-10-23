# Notification Service — fase 1

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
  Persistence/EntityFrameworkCore/  # Solo convencion documentada; implementacion en fase 2
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

Para fase 2 se prefiere NotificationDbContext propio, no el AppDbContext del monolito. Las tablas se configuraran mediante Infrastructure/Persistence/EntityFrameworkCore/Configuration/Extensions/ModelBuilderExtensions.ApplyNotificationConfiguration, llamado desde OnModelCreating. Los repositorios concretos iran en Persistence/EntityFrameworkCore/Repositories. Separar el mapeo del contexto facilita mantener avisos, comunicados y entregas sin mezclar reglas de dominio con EF. No se han creado DbContext, repositorios EF, paquetes de persistencia ni migraciones en esta reorganizacion.

## Implementado

- Avisos por alumno y destinatario: abordaje, desembarque, proximidad, incidencia, SOS y entrega al finalizar el viaje. El SOS conserva prioridad critica y no requiere GPS.
- Comunicados de administrador para su organizacion o una ruta, con requestId idempotente. Roster valida el alcance de la ruta y resuelve los destinatarios.
- Bandeja propia paginada, detalle y marcado de lectura idempotente. La identidad proviene de INotificationUserContext, no de parametros elegidos por el cliente. Para leer avisos de alumnos se consultan los vinculos actuales; no se usa tutorOf como unica autorizacion.
- NotificationDelivery separa aceptacion del canal, reintento, fallo y omision de ReadAt. Un destino push es una suscripcion identificada; aceptar un mensaje no acredita su visualizacion ni su lectura.
- Puertos para persistencia, trabajo de entrega pendiente, Roster, estado activo del viaje, identidad, FCM y SignalR. No hay adaptadores ficticios en produccion.
- Pruebas de dominio, casos de uso mediante el Dispatcher real, contratos, dependencias y arranque del host.

## Contratos con Trip & Tracking

Los seis contratos de entrada estan en Application/Contracts/V1. Infrastructure debera deserializar el payload concreto y traducir IntegrationMessage<T> a NotificationEventMetadata + ReceiveTripEventCommand; Application no referencia RabbitMQ.Client ni BuildingBlocks.Messaging.

Exchange propuesto: `trip-tracking.events`; cola propia: `notification.trip-events.v1`. MessageType/routing keys: `trip.student-boarded.v1`, `trip.student-alighted.v1`, `trip.vehicle-approaching-stop.v1`, `trip.incident-reported.v1`, `trip.sos-raised.v1`, `trip.completed.v1`.

El productor debe acordar estos nombres y agregar OrganizationId desde el propietario real del viaje. No se infiere de un identificador recibido sin verificar ni se cambia de identidad al reintentar: MessageId debe conservar el identificador del hecho original. OccurredAt representa el instante del hecho, no el de sincronizacion; CorrelationId se conserva en cada aviso. El payload evita nombres, direcciones, tokens y coordenadas GPS.

Correspondencias: StudentBoarded/StudentAlighted → StudentId; VehicleApproachingStop → alumnos de la parada y ETA; IncidentReported → alumnos afectados y NotifyGuardians; SosRaised → alumnos afectados y operadores autorizados. TripCompletedV1 requiere Direction y SchoolDeliveredStudentIds: solo las entregas nuevas confirmadas al cerrar un recojo. No debe copiarse sin filtrar el resumen DeliveredStudents del evento de dominio actual: el productor debe enriquecer el contrato con la direccion del viaje y las entregas de esa transicion. El cierre de un retorno no genera otra confirmacion de entrega porque StudentAlighted ya la comunico. No se avisa entrega de alumnos ausentes. Llegar por GPS a una parada no confirma por si solo la entrega de un alumno.

El listado actual de contratos no transporta movimiento continuo. Tracking sigue siendo propietario de GPS, geocercas, ETA, historial y Redis. El canal efimero `trip-tracking.live-positions` y su distribucion por SignalR se integraran aparte de estos avisos durables.

## Limites de infraestructura para las fases siguientes

1. **MySQL 8.4 propio:** NotificationDbContext, migraciones, notificaciones, comunicados, Inbox/ProcessedEvents, trabajo de entrega y suscripciones push. Guardar Inbox + avisos + trabajo pendiente en UNA transaccion, confirmada por la unidad de trabajo. ACK de RabbitMQ solo despues del commit.
2. **Unicidad:** NotificationKey = OrganizationId + SourceMessageId + SourceMessageType + RecipientUserId + SubjectId. SubjectId usa Guid.Empty para avisos sin alumno: un indice MySQL con StudentId nullable no evita todos los duplicados. Announcement requiere unicidad OrganizationId + RequestId. El Inbox necesita identidad por consumidor y mensaje y proteccion ante payloads conflictivos. La comprobacion del core cubre reintentos secuenciales; la garantia concurrente corresponde a los indices y transacciones de fase 2.
3. **Entrega fuera de la transaccion:** INotificationDispatchQueue solo persiste trabajo; un worker posterior expande destinos y llama a FCM/SignalR. Conservar intentos, proximo reintento y errores estables; suscripciones push invalidas/deshabilitadas no eliminan la bandeja. Revalidar permisos, expiracion y vigencia del viaje antes del envio cuando corresponda. No prometer push exactamente una vez: hay una ventana entre la aceptacion externa y el guardado local; el cliente deduplica por notificationId.
4. **Directorios seguros:** Roster es propietario de vinculos y pertenencia al transportista. Resolver destinatarios y enlaces vigentes mediante contratos protegidos o proyecciones con politica explicita de revocacion. Si no puede verificarse el permiso, el adaptador falla; no debe devolver datos viejos como si fueran autorizacion vigente. Trip & Tracking provee IsActive sin compartir tablas ni Redis con el dominio de Notification.
5. **JWT, Gateway y clientes:** mapear sub/NameIdentifier, role/scopes y organizationId emitidos por IAM a NotificationUser. Habilitar /api/v1/notifications/** y el hub por YARP; no exponer broker ni base. Web consume bandeja, SignalR y registra push FCM; el movil del conductor sigue enviando acciones a Tracking.
6. **Redis:** no es persistencia de avisos. Solo incorporar un backplane si se despliegan varias replicas de SignalR. No reemplaza Inbox ni el trabajo durable de MySQL. Docker, secretos, readiness, reintentos/DLQ y observabilidad son trabajo posterior de infraestructura.

NotificationProcessingPolicy permite configurar caducidad de proximidad y tolerancia de reloj; los valores iniciales del core son 5 minutos y 30 segundos respectivamente. Son decisiones iniciales de implementacion ajustables, no mediciones ni valores exigidos por el reporte. Los avisos historicos de abordaje y entrega conservan el instante real y no se descartan como una proximidad efimera.

## Registro y verificacion

AddNotificationApplication() registra CommandServices, QueryServices y los handlers de los casos de uso, y requiere implementaciones reales de los puertos e IUnitOfWork. El host Api conserva registerUseCases: false mientras estos adaptadores no existan; solo expone liveness y OpenAPI. En las fases siguientes, activar los casos de uso una vez registrados los adaptadores. Las pruebas registran dobles exclusivamente en su proyecto.

Con el SDK 10.0.401 fijado por el repositorio:

```powershell
dotnet test src/Services/Notification/tests/RutaSegura.Notification.Tests/RutaSegura.Notification.Tests.csproj -c Release
```

Las pruebas de esta fase no acreditan conexion real con MySQL, RabbitMQ, Firebase ni clientes web. Tampoco solucionan las referencias IAM faltantes en la solucion global. Los adaptadores y las pruebas de integracion se incorporaran en las fases siguientes.

Verificacion focalizada del 22/10/2026 tras separar Api e Interfaces: 71 pruebas aprobadas, sin fallos ni omisiones, en Release. Las 63 existentes pasan por CommandServices/QueryServices; las nuevas verifican assemblers, fronteras, convenciones DDD, referencias de Api/Interfaces en la solucion y separacion del codigo REST frente al host y su configuracion. Incluye arranque del host y comprobacion de que no registra adaptadores falsos. Se ejecuto el proyecto de pruebas por ruta absoluta desde E:\ con el SDK 10.0.300 instalado. La verificacion con el SDK fijado 10.0.401 y la solucion completa sigue pendiente hasta disponer de ese SDK y reconciliar las referencias IAM. No se modifico global.json para realizar esta verificacion.
