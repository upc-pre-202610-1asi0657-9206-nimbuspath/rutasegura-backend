# Notification Service — fases 1, 2 y 3

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
  Messaging/{Inbound,RabbitMq}/    # ACL de eventos, consumidor, reintentos y DLQ
  Integration/                     # Clientes HTTP internos de Administration y Trip & Tracking
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
- Persistencia MySQL 8.4 propia para avisos, comunicados, suscripciones push cifradas, Inbox de eventos procesados, trabajos pendientes y entregas por canal. Repositorios y unidad de trabajo reales, migraciones y Compose local de MySQL/RabbitMQ.
- Idempotencia del evento completo: huella del payload y sus metadatos; confirmacion atomica de Inbox + avisos + trabajo pendiente. Restricciones de base de datos para duplicados concurrentes, incluidos avisos sin alumno, y contador de version para actualizaciones concurrentes.

## Contratos con Trip & Tracking

Los seis contratos de entrada estan en Application/Contracts/V1. Infrastructure deserializa el payload concreto y traduce los metadatos AMQP a NotificationEventMetadata + ReceiveTripEventCommand; Application no referencia RabbitMQ.Client ni BuildingBlocks.Messaging.

Exchange real: `trip-tracking.events`; cola propia: `notification.trip-events.v1`. Las routing keys publicadas actualmente por Trip & Tracking son `trip-tracking.student-boarded`, `trip-tracking.student-alighted`, `trip-tracking.vehicle-approaching-stop`, `trip-tracking.incident-reported`, `trip-tracking.sos-raised` y `trip-tracking.trip-completed`. La version se transporta en `schema-version=1`. Se conserva este Published Language para no romper al productor existente.

`TripTrackingMessageDecoder` es la ACL hacia los tipos internos de Application: `trip.student-boarded.v1`, `trip.student-alighted.v1`, `trip.vehicle-approaching-stop.v1`, `trip.incident-reported.v1`, `trip.sos-raised.v1` y `trip.completed.v1`. Estos nombres canonicos siguen siendo las claves de idempotencia persistidas; no son las routing keys del broker. Notification no referencia ensamblados ni tablas de Tracking en produccion.

El productor agrega OrganizationId desde el propietario real del viaje; si falta, el consumidor envia el evento a cuarentena, nunca infiere el transportista. MessageId conserva el identificador del hecho original. OccurredAt representa el instante del hecho, no el de sincronizacion; CorrelationId se conserva en cada aviso. Los contratos internos y las tablas de avisos no almacenan GPS ni detalles libres de incidencias. El payload AMQP de Tracking puede traer esos campos adicionales: la ACL los descarta para el negocio; DLQ y `.unrouted` conservan el original y requieren acceso restringido y una politica de retencion.

Correspondencias: StudentBoarded/StudentAlighted → StudentId; VehicleApproachingStop → alumnos de la parada y ETA; IncidentReported → alumnos afectados y NotifyGuardians; SosRaised → alumnos afectados y operadores autorizados. TripCompletedV1 requiere Direction y SchoolDeliveredStudentIds: solo las entregas nuevas confirmadas al cerrar un recojo. El mapper de Tracking ya agrega estos campos; no copia sin filtrar el resumen historico DeliveredStudents. El cierre de un retorno no genera otra confirmacion de entrega porque StudentAlighted ya la comunico. No se avisa entrega de alumnos ausentes. Llegar por GPS a una parada no confirma por si solo la entrega de un alumno.

El listado actual de contratos no transporta movimiento continuo. Tracking sigue siendo propietario de GPS, geocercas, ETA, historial y Redis. El canal efimero `trip-tracking.live-positions` y su distribucion por SignalR se integraran aparte de estos avisos durables.

## Persistencia y limites para las fases siguientes

1. **MySQL 8.4 propio (implementado):** NotificationDbContext, migraciones y seis tablas. Inbox + avisos + trabajo pendiente se confirman mediante un unico SaveChanges transaccional. RabbitMQ da ACK solo despues de un commit confirmado, nunca al capturar una excepcion de unicidad.
2. **Unicidad (implementada):** NotificationKey = OrganizationId + SourceMessageId + SourceMessageType + RecipientUserId + SubjectId. SubjectId usa Guid.Empty para avisos sin alumno, con un CHECK de consistencia. Announcement tiene unicidad OrganizationId + RequestId. El Inbox tiene identidad consumer + messageId y una huella para detectar payloads conflictivos. Los indices protegen reintentos concurrentes; ante una carrera se descarta el scope y se reevalua el mensaje en uno nuevo. La comprobacion del core por si sola no sustituye estas restricciones.
3. **Entrega fuera de la transaccion:** INotificationDispatchQueue solo persiste trabajo; un worker posterior expande destinos y llama a FCM/SignalR. Conservar intentos, proximo reintento y errores estables; suscripciones push invalidas/deshabilitadas no eliminan la bandeja. Revalidar permisos, expiracion y vigencia del viaje antes del envio cuando corresponda. No prometer push exactamente una vez: hay una ventana entre la aceptacion externa y el guardado local; el cliente deduplica por notificationId.
4. **Directorios seguros:** Roster es propietario de vinculos y pertenencia al transportista. Resolver destinatarios y enlaces vigentes mediante contratos protegidos o proyecciones con politica explicita de revocacion. Si no puede verificarse el permiso, el adaptador falla; no debe devolver datos viejos como si fueran autorizacion vigente. Trip & Tracking provee IsActive sin compartir tablas ni Redis con el dominio de Notification.
5. **JWT, Gateway y clientes:** mapear sub/NameIdentifier, role/scopes y organizationId emitidos por IAM a NotificationUser. Habilitar /api/v1/notifications/** y el hub por YARP; no exponer broker ni base. Web consume bandeja, SignalR y registra push FCM; el movil del conductor sigue enviando acciones a Tracking.
6. **Redis:** no es persistencia de avisos. Solo incorporar un backplane si se despliegan varias replicas de SignalR. No reemplaza Inbox ni el trabajo durable de MySQL. La fase 3 incorpora RabbitMQ y reintentos/DLQ; el despliegue integrado de todos los servicios, readiness y observabilidad completa siguen pendientes.

NotificationProcessingPolicy permite configurar caducidad de proximidad y tolerancia de reloj; los valores iniciales del core son 5 minutos y 30 segundos respectivamente. Son decisiones iniciales de implementacion ajustables, no mediciones ni valores exigidos por el reporte. Los avisos historicos de abordaje y entrega conservan el instante real y no se descartan como una proximidad efimera.

## Fase 3: RabbitMQ confiable

Flujo implementado: Trip & Tracking confirma agregado + Outbox en su MySQL; su relay publica el payload con metadatos AMQP y publisher confirms; Notification traduce el evento, ejecuta su CommandService y confirma Inbox + avisos + NotificationDispatchJob en un unico SaveChanges. Solo entonces hace ACK. Un mensaje repetido genera un unico aviso por organizacion, hecho, destinatario y alumno (sin alumno para operadores); no vuelve a resolver el directorio si ya existe el Inbox con la misma huella.

El trabajo pendiente es durable pero aun no se expande en entregas FCM/SignalR. Esa expansion y los envios externos pertenecen a la fase de canales, siempre fuera de la transaccion de consumo. No se agregan Redis, frontend ni endpoints publicos en esta fase.

### Reintentos y errores

- Cola principal durable de tipo quorum; ACK manual y prefetch 20 por defecto. Un scope/DbContext nuevo por intento.
- Fallos de infraestructura, timeouts, errores de persistencia y carreras de unicidad: reintentos a 1, 5 y 30 segundos. Tras agotarlos, cola `notification.trip-events.v1.dlq`.
- Formato/version/metadatos invalidos, falta de organizacion, audiencia invalida o reutilizacion conflictiva de MessageId: DLQ sin ejecutar mas intentos de negocio.
- La transferencia a retry/DLQ usa mensaje persistente, `mandatory=true` y publisher confirms antes del ACK del original. Conserva MessageId, CorrelationId, OccurredAt y payload; agrega `x-notification-retry`, `x-notification-original-routing-key` y `x-notification-error-code`.
- Las colas de espera son quorum con TTL y dead-lettering `at-least-once` hacia un exchange de retorno exclusivo. No se vuelve a publicar en el exchange de dominio al reintentar (evita duplicar entregas a otros servicios).
- Una transferencia sin confirmacion o un ACK fallido cierra la sesion; el broker conserva los originales no confirmados. Puede haber entregas repetidas; la unicidad durable de MySQL evita duplicar avisos. No se promete exactamente-una-vez en el transporte.
- `x-delivery-limit=-1` evita que un corte de conexion consuma el presupuesto de intentos de negocio. Configurar capacidad, alertas de disco y politicas operativas antes de produccion; no se borran eventos al reiniciar el worker.

### Coordinacion con el productor

Se reutilizan el EfUnitOfWork y el OutboxProcessor existentes de Trip & Tracking, sin publicacion directa desde el controller. Se refuerza la publicacion de eventos con `mandatory=true`. El identificador del domain event se guarda en el Outbox y se reutiliza en cada intento.

El cierre agrega `direction` y `schoolDeliveredStudentIds` al contrato publicado, sin eliminar sus campos anteriores. El dominio captura solo los alumnos que estaban abordados justo antes del cierre de recojo. No repite descensos previos ni entregas de un retorno. Los eventos de cierre antiguos que no contienen estos campos van a DLQ para revision; no se deducen destinatarios a partir del resumen historico `deliveredStudents`.

Ambos servicios declaran el exchange con el mismo alternate exchange `trip-tracking.events.unrouted`, evitando errores de precondicion segun el orden de arranque. Se debe provisionar la cola/bindings de Notification ANTES de habilitar productores: un confirm solo acredita aceptacion por el broker, no procesamiento por Notification. Los mensajes que ya fueron a `.unrouted` NO se mueven automaticamente al crear el binding. Se monitorean y se reprocesan por operacion controlada, preservando su identidad, sin republicar indiscriminadamente eventos de otros consumidores.

Una cola preexistente de tipo classic no puede convertirse en quorum redeclarandola. Planificar drenaje/migracion o usar otro nombre de cola, conservando el Inbox; no borrar una cola con mensajes para resolver un error 406. Los TTL forman parte de la declaracion de las colas; cambiar su configuracion requiere gestionar las colas antiguas y sus pendientes.

### Configuracion y dependencias reales

Aplicar primero las migraciones de fase 2. Mantener secretos fuera de appsettings y del repositorio, mediante variables de entorno/secret manager:

```text
ConnectionStrings__NotificationDatabase=<conexion MySQL propia>
NotificationPersistence__PushTokenEncryptionKey=<32 bytes en base64>
NotificationRabbitMq__Enabled=true
NotificationRabbitMq__HostName=<broker compartido con Trip & Tracking>
NotificationRabbitMq__Port=5672
NotificationRabbitMq__VirtualHost=/
NotificationRabbitMq__UserName=<usuario propio>
NotificationRabbitMq__Password=<secreto>
NotificationDependencies__Administration__BaseUrl=https://administration.internal/
NotificationDependencies__Administration__ServiceToken=<secreto de servicio, minimo 32 caracteres>
NotificationDependencies__TripTracking__BaseUrl=https://trip-tracking.internal/
NotificationDependencies__TripTracking__ServiceToken=<mismo secreto configurado en Tracking>
```

Tracking configura `InternalServices__NotificationToken` para el endpoint implementado `GET /internal/v1/trips/{tripId}/notification-context?organizationId={organizationId}`. Requiere `X-Service-Token`; valida tenant antes de responder `{ organizationId, tripId, isActive }` sin GPS ni alumnos. Si falta el secreto, el endpoint deniega acceso. No publicar `/internal/**` mediante el Gateway. Restringir red de servicios y rotar secretos; no reutilizar JWT de familias como credencial de servicio.

El cliente HTTP de Administration ya esta implementado en Notification, pero **Administration/Roster todavia no implementa estos endpoints en este repositorio**. Contrato minimo que debe coordinarse con su responsable, tambien protegido con `X-Service-Token`:

| Operacion | Solicitud JSON | Respuesta JSON |
| --- | --- | --- |
| `POST /internal/v1/notifications/recipients/students` | `{ organizationId, studentIds: [] }` | `{ organizationId, recipients: [{ organizationId, userId, studentId }] }` |
| `POST /internal/v1/notifications/recipients/operators` | `{ organizationId }` | `{ organizationId, recipients: [{ organizationId, userId, studentId: null }] }` |

Las listas deben contener exclusivamente vinculos vigentes de esa organizacion. Una lista vacia solo es valida como respuesta autoritativa; 404/401/5xx, timeouts o respuestas de otro tenant generan fallo, nunca una audiencia vacia ficticia. Para la fase REST se reservan `POST .../recipients/announcement` con `{organizationId, routeId}` (validar pertenencia de ruta) y `POST /internal/v1/notifications/student-links` con `{organizationId,userId}` y respuesta `{organizationId,userId,studentIds:[]}`. El worker no registra usuarios ficticios; su contexto de usuario es nulo.

Los clientes HTTP exigen HTTPS y deshabilitan redirecciones para no filtrar tokens. En desarrollo local exclusivamente se puede configurar `NotificationDependencies__Administration__AllowInsecureHttp=true` y el equivalente para TripTracking. El consumidor admite AMQPS con `NotificationRabbitMq__UseTls=true`, puerto y `TlsServerName` correspondientes; no deshabilitar validacion de certificados. Restringir usuarios/permisos del broker a exchanges/colas necesarios. DLQ y `.unrouted` contienen payload original potencialmente sensible: no exponerlos a clientes, no registrar cuerpos ni credenciales y acordar retencion/eliminacion segura segun politica del proyecto.

El Compose local de `infrastructure/compose.yaml` agrega RabbitMQ 4.1 y MySQL 8.4 con puertos solo loopback y volumenes propios. Es un recurso de desarrollo: apuntar AMBOS servicios al mismo broker; no levantar dos brokers aislados para una misma integracion. No configura TLS ni sustituye el despliegue productivo.

### Pruebas de fase 3

```powershell
./scripts/Test-NotificationMessaging.ps1
```

El script crea contenedores efimeros de MySQL 8.4 y RabbitMQ 4.1 con credenciales aleatorias y puertos locales dinamicos; cada caso usa bases y topologia propias. Retira exclusivamente sus contenedores al finalizar. Sin las variables de prueba, los casos de integracion se omiten explicitamente; las pruebas unitarias del decoder, coordinador y clientes HTTP siguen funcionando sin Docker.

Se comprueban seis eventos reales, duplicados, reintentos recuperables/agotados, DLQ, identidad conflictiva, ventanas de ACK perdido, carreras entre consumidores, reconexion, confirmacion fallida por mensaje no enrutable y el flujo EfUnitOfWork → OutboxProcessor → RabbitMQ → Notification/MySQL. Los directorios de pruebas son dobles exclusivos de tests, no acreditan que Administration ya tenga disponible su API. La entrega a FCM/SignalR no se prueba ni se implementa aqui.

Referencias tecnicas: [cliente .NET de RabbitMQ](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), [ACK y publisher confirms](https://www.rabbitmq.com/docs/confirms), [dead-lettering y colas quorum](https://www.rabbitmq.com/docs/dlx).

Verificacion focalizada de fase 3 (23/10/2026), en Release: **331 pruebas aprobadas, sin fallos ni omisiones**. Notification: 78 de core/contratos/host + 31 de persistencia + 53 de mensajeria. De mensajeria, 12 usan MySQL 8.4 y RabbitMQ 4.1 reales; las restantes prueban ACL, contratos publicados reales, coordinacion de ACK, HTTP y registro. Tracking: 90 de dominio + 72 de aplicacion + 7 del endpoint interno de contexto/autorizacion. Ambos hosts compilan sin advertencias. Se uso el SDK 10.0.300 instalado desde E:\ con proyectos por ruta absoluta; no se cambio el SDK fijado 10.0.401. No se ejecuto toda la solucion ni las demas pruebas de integracion de Tracking, ni se valida disponibilidad de Administration, FCM o clientes. Se retiraron solo contenedores efimeros de pruebas, sin tocar volumenes existentes. No se ejecutaron comandos Git.

## Registro y verificacion

AddNotificationApplication() registra CommandServices, QueryServices y los handlers de los casos de uso. La fase 2 aporta repositorios e IUnitOfWork reales cuando se configura ConnectionStrings:NotificationDatabase y una clave de tokens externa. Con NotificationRabbitMq:Enabled=true, Api activa los casos de uso y el worker con directorios HTTP reales; sin ello conserva el modo liveness/OpenAPI. No migra la base automaticamente ni habilita endpoints REST de negocio. El contexto del worker carece de usuario y los casos de uso REST siguen denegados hasta incorporar JWT. Las pruebas registran dobles de directorios exclusivamente en sus proyectos.

Con el SDK 10.0.401 fijado por el repositorio:

```powershell
dotnet test src/Services/Notification/tests/RutaSegura.Notification.Tests/RutaSegura.Notification.Tests.csproj -c Release
```

La suite RutaSegura.Notification.Persistence.Tests verifica migraciones, persistencia, rollback, indices, cifrado, aislamiento y carreras contra MySQL 8.4 real mediante scripts/Test-NotificationPersistence.ps1. Sin NOTIFICATION_TEST_MYSQL, los casos de integracion se omiten explicitamente. La suite de mensajeria siguiente agrega RabbitMQ real; ninguna de estas suites acredita Firebase, clientes web ni toda la solucion global.

Verificacion focalizada del 22/10/2026 tras fase 2: 109 pruebas aprobadas en Release, sin fallos ni omisiones: 78 de dominio/aplicacion/contratos/host y 31 de persistencia, modelo, cifrado y registro. De estas ultimas, 20 se ejecutan contra MySQL 8.4 real y verifican migraciones, atomicidad, unicidad, aislamiento y concurrencia; las otras 11 no requieren un servidor. Los contenedores temporales de prueba se retiraron al terminar, sin tocar bases o volumenes existentes. La base permanente de Compose queda preparada para configurarla con secretos propios; no se han almacenado credenciales reales en el repositorio. Se verifico con el SDK 10.0.300 instalado, ejecutando los proyectos por ruta absoluta desde E:\. La verificacion con el SDK fijado 10.0.401 y la solucion completa sigue pendiente hasta disponer de ese SDK y reconciliar las referencias IAM de los proyectos globales de pruebas. No se modifico global.json ni se ejecutaron comandos Git.
