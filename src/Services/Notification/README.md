# Notification Service — fases 1 a 4

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
  Channels/                        # Worker, coordinacion MySQL y adaptador Firebase Admin
RutaSegura.Notification.Interfaces/
  Rest/
    Controllers/           # Bandeja, lectura, comunicados y suscripciones push
    Resources/             # Contratos REST; no exponen entidades
    Transform/             # Assemblers Resource -> Command y Result -> Resource
RutaSegura.Notification.Api/
  Program.cs
  appsettings.json
  Properties/launchSettings.json
  Security/                # JWT y contexto del usuario HTTP
  Realtime/                # Hub SignalR y adaptador de avisos genericos
  Errors/                  # Traduccion segura a ProblemDetails
```

Los servicios publicos REST exponen Handle(command/query, cancellationToken). Sus implementaciones delegan al Dispatcher compartido para preservar validacion, logging y una sola confirmacion de la unidad de trabajo; los handlers con la logica de cada caso quedan bajo Internal/CommandServices o Internal/QueryServices. El servicio de canales es un orquestador exclusivo del worker: confirma explicitamente la expansion y el resultado del envio por separado, sin envolver llamadas externas en una transaccion MySQL.

Commands y Queries permanecen en Application/Model: implementan los contratos CQRS de BuildingBlocks.Application. Ponerlos en Domain como estan hoy introduciria una dependencia de Domain hacia Application. Los Resources son exclusivos de Interfaces; los Results de Application son independientes de HTTP. Interfaces transforma resultados mediante assemblers y solo referencia Application, no Domain ni Infrastructure. Api referencia Application, Infrastructure e Interfaces y concentra arranque, registro, JWT, SignalR, OpenAPI y configuracion; registra los controllers de Interfaces cuando se habilita la API.

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
3. **Entrega fuera de la transaccion (fase 4):** INotificationDispatchQueue persiste trabajo; el worker expande destinos y llama a FCM/SignalR. Conserva intentos, proximo reintento y errores estables; suscripciones invalidas/deshabilitadas no eliminan la bandeja. Revalida permisos, expiracion y vigencia del viaje cuando corresponde. No promete push exactamente una vez: hay una ventana entre aceptacion externa y guardado local; el cliente deduplica por notificationId.
4. **Directorios seguros:** Roster es propietario de vinculos y pertenencia al transportista. Resolver destinatarios y enlaces vigentes mediante contratos protegidos o proyecciones con politica explicita de revocacion. Si no puede verificarse el permiso, el adaptador falla; no debe devolver datos viejos como si fueran autorizacion vigente. Trip & Tracking provee IsActive sin compartir tablas ni Redis con el dominio de Notification.
5. **JWT y Gateway (fase 4):** se mapean sub/NameIdentifier, role y organizationId de IAM a NotificationUser; YARP publica solo rutas explicitas REST y hub. El cliente web todavia debe consumirlas y registrar push. El movil del conductor sigue enviando acciones a Tracking; no necesita cambios para implementar este backend.
6. **Redis:** no es persistencia de avisos. La fase 4 permite configurar un backplane para varias replicas SignalR; no reemplaza Inbox ni MySQL. El despliegue integrado de todos los servicios, validacion multi-replica, readiness y observabilidad completa siguen pendientes.

NotificationProcessingPolicy permite configurar caducidad de proximidad y tolerancia de reloj; los valores iniciales del core son 5 minutos y 30 segundos respectivamente. Son decisiones iniciales de implementacion ajustables, no mediciones ni valores exigidos por el reporte. Los avisos historicos de abordaje y entrega conservan el instante real y no se descartan como una proximidad efimera.

## Fase 3: RabbitMQ confiable

Flujo implementado: Trip & Tracking confirma agregado + Outbox en su MySQL; su relay publica el payload con metadatos AMQP y publisher confirms; Notification traduce el evento, ejecuta su CommandService y confirma Inbox + avisos + NotificationDispatchJob en un unico SaveChanges. Solo entonces hace ACK. Un mensaje repetido genera un unico aviso por organizacion, hecho, destinatario y alumno (sin alumno para operadores); no vuelve a resolver el directorio si ya existe el Inbox con la misma huella.

La fase 3 finalizaba al persistir el trabajo pendiente. La fase 4 siguiente implementa su expansion y los envios FCM/SignalR, siempre fuera de la transaccion de consumo. No se modifica frontend ni movil.

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

Se comprueban seis eventos reales, duplicados, reintentos recuperables/agotados, DLQ, identidad conflictiva, ventanas de ACK perdido, carreras entre consumidores, reconexion, confirmacion fallida por mensaje no enrutable y el flujo EfUnitOfWork → OutboxProcessor → RabbitMQ → Notification/MySQL. Los directorios son dobles exclusivos de tests, no acreditan disponibilidad de Administration. La fase 4 amplia esta suite con SignalR/HTTP reales y un transporte FCM de prueba, sin contactar Firebase.

Referencias tecnicas: [cliente .NET de RabbitMQ](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), [ACK y publisher confirms](https://www.rabbitmq.com/docs/confirms), [dead-lettering y colas quorum](https://www.rabbitmq.com/docs/dlx).

Verificacion focalizada de fase 3 (23/10/2026), en Release: **331 pruebas aprobadas, sin fallos ni omisiones**. Notification: 78 de core/contratos/host + 31 de persistencia + 53 de mensajeria. De mensajeria, 12 usan MySQL 8.4 y RabbitMQ 4.1 reales; las restantes prueban ACL, contratos publicados reales, coordinacion de ACK, HTTP y registro. Tracking: 90 de dominio + 72 de aplicacion + 7 del endpoint interno de contexto/autorizacion. Ambos hosts compilan sin advertencias. Se uso el SDK 10.0.300 instalado desde E:\ con proyectos por ruta absoluta; no se cambio el SDK fijado 10.0.401. No se ejecuto toda la solucion ni las demas pruebas de integracion de Tracking, ni se valida disponibilidad de Administration, FCM o clientes. Se retiraron solo contenedores efimeros de pruebas, sin tocar volumenes existentes. No se ejecutaron comandos Git.

## Fase 4: API y canales

Implementada dentro de Notification y ApiGateway. No se modifica el codigo productivo de IAM, Administration/Roster ni Trip & Tracking en esta fase. No se conectan sus tablas ni se sustituyen sus directorios por datos ficticios. La base sigue siendo MySQL 8.4 propia y conserva las seis tablas y la migracion inicial: esta fase no requiere otra migracion.

### Contrato para la web

Todas las rutas siguientes requieren `Authorization: Bearer <JWT de IAM>`. UserId y OrganizationId se obtienen exclusivamente de sus claims, no de headers de identidad ni del cuerpo del cliente.

| Metodo y ruta | Funcion / contrato |
| --- | --- |
| `GET /api/v1/notifications?offset=0&limit=25&unreadOnly=false` | Lista propia; limite 1–100, orden descendente por creacion. |
| `GET /api/v1/notifications/{id}` | Detalle propio sujeto a autorizacion vigente. |
| `PATCH /api/v1/notifications/{id}/read` | Marca lectura idempotente; devuelve el aviso. No confirma ni cancela el envio externo. |
| `POST /api/v1/announcements` | ADMIN con vinculacion operativa vigente; `{ requestId, title, body, routeId? }`. 201 al crear, 200 al repetir la misma solicitud, 409 si cambia su contenido. Los destinatarios lo leen en su bandeja. |
| `GET /api/v1/push-subscriptions` | Suscripciones habilitadas del usuario actual; nunca devuelve tokens. |
| `PUT /api/v1/push-subscriptions/{installationId}` | Registra/actualiza/reactiva la instalacion propia: `{ token, platform }`, platform `Web`, `Android` o `Ios`. Devuelve ID de suscripcion y metadatos sin token. |
| `DELETE /api/v1/push-subscriptions/{subscriptionId}` | Desactiva una suscripcion propia, idempotente, 204. Una suscripcion ajena responde 404. |
| `/hubs/notifications` | Hub SignalR; negociacion `POST /hubs/notifications/negotiate` y transporte WebSocket/SignalR. |

Errores esperados: 400 solicitud invalida, 401 identidad/token invalido, 403 permiso insuficiente, 404 recurso ajeno/inexistente, 409 conflicto y 503 dependencia no disponible. Los errores de aplicacion exponen `code` en ProblemDetails, sin SQL, tokens o cuerpos de terceros. Los rechazos del middleware JWT/Gateway pueden no incluir cuerpo; la web debe manejar su estado HTTP. No se implementa CRUD administrativo de comunicados ni borrado de avisos: el alcance actual es publicacion y lectura en la bandeja.

La instalacion debe conservar un GUID estable por cuenta/contexto de transportista y navegador. Hay un limite operativo de 20 suscripciones habilitadas por usuario. Al cambiar de cuenta: desactivar la suscripcion de la sesion anterior y renovar/eliminar el registro FCM del dispositivo segun el SDK cliente. No se transfiere una instalacion o token de otro propietario mediante un PUT: los indices producen 409. El token cifrado y su hash unico se conservan incluso al desactivar; obtener un registro nuevo evita reutilizar el destino de otra cuenta.

### Autenticacion y autorizacion

- Se verifica firma HS256, expiracion, usuario GUID, `organizationId` GUID y rol `PARENT`, `ADMIN` o `DRIVER`. Se aceptan `sub`, `nameid` o el claim URI NameIdentifier de IAM; se normaliza `role` al claim de rol de ASP.NET.
- `Jwt:Secret` debe coincidir con el secreto configurado en IAM y tener al menos 32 bytes. No se emiten tokens desde Notification ni Gateway. Issuer/Audience se validan cuando se configuran; IAM actual no los emite, por lo que deben acordarse antes de exigirlos. No inventar valores solo en el consumidor.
- API, detalle, lectura y envios revalidan vinculos tutor–alumno. Los comunicados revalidan su audiencia por organizacion/ruta; los SOS sin alumno revalidan operadores. Publicar requiere tanto rol ADMIN como presencia actual en `recipients/operators`.
- Una dependencia caida o con respuesta invalida no equivale a permiso concedido ni a una audiencia vacia valida. REST devuelve 503; el worker reintenta sin enviar y finalmente registra fallo si no puede comprobar el acceso.
- Las paginas pueden acortarse cuando se revoca el acceso a comunicados/SOS sin alumno despues de aplicar la paginacion relacional. No se devuelve total ni cursor estable. El cliente debe avanzar offset por el limite solicitado, no por la cantidad recibida; mejorar esta paginacion con un contrato de alcance consultable en lote sigue pendiente antes de grandes historiales.

### SignalR

El servidor agrega la conexion exclusivamente al grupo `notification:{organizationId}:{userId}`, derivado del JWT. No hay metodos JoinGroup/Subscribe con identificadores elegidos por el cliente ni broadcasts de contenido al transportista completo. Se cierra la conexion al expirar la autenticacion. Query string `access_token` se acepta solo bajo `/hubs/notifications`; nunca sirve para autenticar REST.

Evento saliente: `NotificationAvailable`, payload `{ notificationId, createdAt }`. No transporta alumno, GPS, titulo ni contenido de incidencias/comunicados. La web solicita el detalle por REST y se vuelve a comprobar su permiso. Al conectar/reconectar debe recargar la bandeja: SignalR no es almacenamiento ni garantiza que una conexion desconectada reciba cada aviso. La aceptacion por el hub no significa que haya un navegador conectado ni acredita lectura. Deduplicar por notificationId.

CORS exige origenes explicitos; el hub tambien rechaza un Origin de navegador no autorizado. No usar `*` con credenciales. El logging de diagnosticos HTTP y YARP queda en Warning para no registrar URLs de conexion con tokens.

Por defecto se usa una sola instancia, sin Redis. `NotificationRealtime:RedisConnection` habilita el backplane de SignalR con prefijo `rutasegura.notification`; no se leen claves GPS de Tracking ni se guardan avisos en Redis. Antes de varias replicas deben verificarse Redis real, afinidad/negociacion, proteccion de claves, red y failover. Esa validacion multi-replica no se ha realizado; el backplane no es una cola durable y no sustituye la bandeja.

### Worker de canales y FCM

`NotificationChannelWorker` selecciona primero SOS e incidencias y crea sus entregas Realtime y las Push habilitadas, confirmando entregas + ExpandedAt atomicamente. Procesa hasta 4 trabajos concurrentes por defecto (configurable 1–16) para no bloquear todo el lote ante un proveedor lento. Recarga cada trabajo en su propio scope dentro de un lock de sesion MySQL `GET_LOCK`, sobre conexion dedicada sin pooling, liberado al terminar o al detectar MySQL el cierre de la conexion. Esto coordina workers contra la misma base/primario MySQL sin mantener una transaccion durante la llamada externa; no es una solucion para varios primarios independientes. La prioridad de seleccion no es preempcion de envios en curso ni garantia de latencia.

Antes de cada envio se verifica destinatario vigente, caducidad y viaje activo para proximidad. Se comprueba de nuevo que la suscripcion siga habilitada y se usa su token actual. El envio precede a una nueva confirmacion local de su resultado. Los fallos transitorios se persisten con backoff de 5, 10, 20, 40... segundos (tope 300); maximo 5 intentos por defecto. Al agotarlos se conserva la entrega Failed y el aviso sigue en la bandeja. Un destino revocado, deshabilitado o aviso caducado queda Skipped.

`FcmPushNotificationSender` usa Firebase Admin .NET 3.7 y ADC/identidad de servicio, nunca un secreto de Firebase en la web. Envia titulo generico "RutaSegura", cuerpo generico e ID de notificacion; no envia contenido personal a la pantalla bloqueada. TTL Web/Android de 60 segundos, notificacion etiquetada por ID y prioridad alta para SOS. `UNREGISTERED`/`SENDER_ID_MISMATCH` deshabilitan la suscripcion; cuota, indisponibilidad y fallos internos son reintentables. `INVALID_ARGUMENT` no desactiva automaticamente una suscripcion, porque puede indicar un payload/configuracion invalida.

La API actual conserva el contrato de **registration tokens** de fase 2. Firebase 3.7 todavia los soporta, aunque los declara obsoletos y recomienda FID. La compatibilidad esta aislada explicitamente en el adaptador. Antes de adoptar FID en la web se debe acordar y persistir un tipo de destino, migrar las suscripciones y modificar conjuntamente el contrato/cliente; no mezclar identificadores sin conocer su tipo. No se cambia silenciosamente la interpretacion de los tokens ya cifrados. [Contrato actual de Firebase y migracion a FID](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk).

Una caida despues de aceptacion externa y antes del commit puede repetir un envio. No se promete push exactamente una vez; notificacion y entregas logicas son unicas, el cliente deduplica IDs y la etiqueta ayuda a reemplazar visualizaciones repetidas. ProviderAccepted no significa recibido, mostrado ni leido. Un push que no llegue no elimina el aviso persistente. No se garantiza una entrega de SOS por un unico canal: la operacion debe consultar el estado autorizado del viaje.

### Activacion

Mantener la conexion MySQL, clave de cifrado y endpoints internos seguros de fases 2/3. La clave AES debe ser identica en API, workers y replicas que usan esa base; no reemplazarla ciegamente: rotarla requiere conservar la anterior y migrar los tokens cifrados. No se implementa un keyring multi-version en esta fase. Adicionalmente configurar por entorno/secret manager:

```text
NotificationApi__Enabled=true
Jwt__Secret=<mismo secreto que IAM, minimo 32 bytes>
Cors__AllowedOrigins__0=https://tu-web.example
NotificationChannels__Enabled=true
NotificationFcm__Enabled=true
NotificationFcm__ProjectId=<proyecto Firebase>
GOOGLE_APPLICATION_CREDENTIALS=<ruta externa protegida, solo si no hay identidad/ADC del entorno>
```

Para desarrollar sin Firebase, dejar `NotificationFcm__Enabled=false`: el worker procesa SignalR, pero no crea nuevas entregas push. No aparenta un envio push exitoso. Los destinos se fijan al expandir el trabajo: habilitar push despues no genera retroactivamente entregas para trabajos ya expandidos; las entregas existentes deshabilitadas pueden quedar Skipped y requerir reproceso controlado.

RabbitMQ se activa aparte con `NotificationRabbitMq__Enabled=true`. Se puede usar API/SignalR para comunicados sin broker, siempre con directorios reales configurados. Canales requiere API habilitada; API no requiere RabbitMQ. Sin configurar funciones el host conserva solo liveness/OpenAPI, sin adaptadores ficticios. Aplicar migraciones antes de arrancar; no se aplican automaticamente.

En Gateway:

```text
NotificationProxy__Enabled=true
Jwt__Secret=<mismo secreto de IAM/Notification>
Cors__AllowedOrigins__0=https://tu-web.example
ReverseProxy__Clusters__notification__Destinations__primary__Address=https://tu-notification-interno/
```

En desarrollo la direccion predeterminada es `http://localhost:5182/`, con proxy deshabilitado hasta configurarlo. YARP conserva las rutas y el JWT, valida el token en el borde y Notification lo valida nuevamente. Publica exclusivamente notifications, announcements, push-subscriptions y hub (incluida negociacion/WebSocket). No publica `/internal/**`, broker, base ni rutas de otros servicios. No se reconfigura IAM/Administration/Tracking a traves del Gateway en esta fase. TLS/HTTPS del despliegue y certificados deben configurarse en servidor/ingress; no se deshabilita validacion de certificados ni se crean secretos en appsettings.

### Pendientes por responsable, incluidas fases anteriores

| Responsable | Pendiente concreto | Impacto |
| --- | --- | --- |
| Administration/Roster | Implementar y proteger los cuatro endpoints de directorio descritos en fase 3; devolver alumnos, tutores, operadores y audiencias vigentes, y validar organizacion/ruta. `student-links` debe responder tambien para operadores sin alumnos con lista autoritativa vacia. | Sin esto no hay autorizacion ni destinatarios reales. No es un permiso para consultar tablas ajenas. |
| IAM | Corregir el registro publico que acepta RoleTier y OrganizationId sin un flujo autorizado de concesion; controlar altas/invitaciones, pertenencia y roles. | Bloqueo de seguridad para produccion: un JWT firmado no corrige una identidad autoasignada. El chequeo actual de operadores en Notification es defensa adicional, no reemplaza esa correccion. |
| IAM + equipo | Garantizar organizationId autorizado para las sesiones que usan Notification; acordar firma, issuer/audience, expiracion y revocacion. Si un tutor usa varios transportistas, definir contexto autorizado por sesion. | No inferir tenant ni permitir elegirlo libremente en el body/header. Tokens sin tenant se rechazan. |
| Responsable Firebase + web | Provisionar proyecto, identidad/credenciales server, registro cliente, permiso del navegador, VAPID y service worker sobre HTTPS; acordar tokens/FID y gestion de logout/rotacion. | Adaptador implementado, pero no se ha probado envio a Firebase/dispositivos reales. No se modifica frontend ni movil aqui. |
| Trip & Tracking / integracion | Integrar en la version conjunta los contratos de cierre con direccion + entregas nuevas y OrganizationId autoritativo, preservando ID/fecha/correlacion del Outbox. | Eventos antiguos o incompletos permanecen en DLQ; no inferir datos al reprocesar. El codigo local de fase 3 ya contempla la coordinacion. |
| Infraestructura / equipo | Usar el mismo broker/exchange, provisionar bindings antes de productores, revisar y reprocesar `.unrouted`/DLQ preservando identidad. No convertir classic a quorum borrando mensajes. | Pendiente operativo de fase 3: crear un binding no recupera automaticamente el backlog anterior. |
| Infraestructura / equipo | Secretos/rotacion, permisos minimos MySQL y RabbitMQ, TLS en todos los tramos requeridos, retencion de Inbox/DLQ/logs, backups, capacidad y alertas. | Contenedores de pruebas y Compose local no constituyen un despliegue productivo. No hay retencion automatica del Inbox ni reproceso administrativo de entregas fallidas. |
| Notification / equipo | Readiness integral, telemetria de colas/entregas, pruebas de carga/latencia, refinamiento de paginacion con audiencias revocadas y pruebas multi-replica/backplane. | No se acreditan todavia los SLO del reporte ni disponibilidad/escala de produccion. |
| Equipo | Instalar/verificar SDK fijado 10.0.401 y runtime parcheado del despliegue; reconciliar pruebas globales. | Se valida focalizadamente con SDK 10.0.300 instalado, sin cambiar global.json ni otros servicios. |

### Verificacion de fase 4

Resultado focalizado del 23/10/2026: **205 pruebas aprobadas en Release, 0 fallos y 0 omisiones**: 102 de core/API/JWT/Gateway, 40 de persistencia/canales y 63 de mensajeria/FCM/E2E. De persistencia, 29 casos usan MySQL real; de mensajeria, 13 usan MySQL y RabbitMQ reales. Se verifican HTTP, negociacion y WebSockets reales, incluido el proxy YARP. Notification.Api y ApiGateway compilan sin advertencias. La matriz completa se volvio a ejecutar satisfactoriamente tras ajustar la preparacion de tokens del fixture E2E para usar el protector del propio host. No se valida Firebase real ni toda la solucion global. Los contenedores efimeros se retiraron, sin tocar bases/volumenes existentes. No se ejecutaron comandos Git.

Ejecutar `scripts/Test-NotificationChannels.ps1` con Docker activo y el SDK del repositorio. Ejecuta core/API/Gateway, MySQL y mensajeria en Release; las pruebas crean sus propios contenedores/bases/topologias y los retiran al finalizar. Sin variables de infraestructura los casos MySQL/RabbitMQ se omiten explicitamente; no confundir una suite omitida con una integracion validada.

La prueba de extremo a extremo usa RabbitMQ y MySQL reales, host HTTP/SignalR real, JWT y worker reales: evento → Inbox/aviso/trabajo → bandeja → hint WebSocket → adaptador FCM → lectura persistida y rechazo tras revocacion. Solo el directorio remoto y el transporte hacia Firebase se reemplazan por dobles exclusivos de tests. Otra prueba conecta HTTP, negociacion y WebSockets atravesando YARP y comprueba aislamiento entre usuarios/transportistas. Nunca se envian avisos a dispositivos reales durante estas pruebas. El SDK Firebase/ADC, APIs de companeros, cliente web y backplane multi-replica requieren verificacion posterior.

Referencias: [autorizacion SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0), [WebSockets en YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0), [Firebase Admin](https://firebase.google.com/docs/admin/setup). El backplane incorpora pin de MessagePack 2.5.303 para corregir la dependencia transitiva afectada; no se habilita el protocolo MessagePack del hub. [Aviso del parche](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h).

## Registro y verificacion

AddNotificationApplication() registra CommandServices, QueryServices y handlers. La fase 2 aporta repositorios e IUnitOfWork reales con ConnectionStrings:NotificationDatabase y clave externa. En fase 4, NotificationApi:Enabled, NotificationRabbitMq:Enabled y NotificationChannels:Enabled activan sus funciones por separado; los casos de uso se registran si cualquiera esta habilitada. Canales requiere API para el hub autenticado. Sin activar funciones, el host conserva liveness/OpenAPI. No migra la base automaticamente. El worker no suplanta un usuario; REST obtiene su identidad del JWT validado. Los dobles de directorios existen solo en tests.

Con el SDK 10.0.401 fijado por el repositorio:

```powershell
dotnet test src/Services/Notification/tests/RutaSegura.Notification.Tests/RutaSegura.Notification.Tests.csproj -c Release
```

La suite RutaSegura.Notification.Persistence.Tests verifica migraciones, persistencia, rollback, indices, cifrado, aislamiento y carreras contra MySQL 8.4 real mediante scripts/Test-NotificationPersistence.ps1. Sin NOTIFICATION_TEST_MYSQL, los casos de integracion se omiten explicitamente. La suite de mensajeria siguiente agrega RabbitMQ real; ninguna de estas suites acredita Firebase, clientes web ni toda la solucion global.

Verificacion focalizada del 22/10/2026 tras fase 2: 109 pruebas aprobadas en Release, sin fallos ni omisiones: 78 de dominio/aplicacion/contratos/host y 31 de persistencia, modelo, cifrado y registro. De estas ultimas, 20 se ejecutan contra MySQL 8.4 real y verifican migraciones, atomicidad, unicidad, aislamiento y concurrencia; las otras 11 no requieren un servidor. Los contenedores temporales de prueba se retiraron al terminar, sin tocar bases o volumenes existentes. La base permanente de Compose queda preparada para configurarla con secretos propios; no se han almacenado credenciales reales en el repositorio. Se verifico con el SDK 10.0.300 instalado, ejecutando los proyectos por ruta absoluta desde E:\. La verificacion con el SDK fijado 10.0.401 y la solucion completa sigue pendiente hasta disponer de ese SDK y reconciliar las referencias IAM de los proyectos globales de pruebas. No se modifico global.json ni se ejecutaron comandos Git.
