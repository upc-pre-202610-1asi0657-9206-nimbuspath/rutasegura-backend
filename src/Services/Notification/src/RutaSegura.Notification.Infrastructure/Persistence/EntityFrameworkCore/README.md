# Persistencia propia de Notification — fase 2

EF Core 10 y el proveedor oficial MySql.EntityFrameworkCore 10.0.12, para MySQL 8.0/InnoDB en el Compose actualizado.
No se comparte DbContext, tablas, claves foraneas ni conexiones con IAM, Administration o Tracking.
La base de desarrollo se llama `rutasegura_notification`; Redis no participa en esta persistencia.

## Organizacion

- `Configuration/NotificationDbContext.cs`: contexto scoped y preparacion de hash de tokens y versiones de concurrencia.
- `Configuration/Extensions/ModelBuilderExtensions.cs`: `ApplyNotificationConfiguration`, llamado desde `OnModelCreating`.
- `Configuration/NotificationDbContextFactory.cs`: creacion del contexto para las herramientas EF, sin iniciar el host.
- `Repositories/`: implementaciones de los contratos de Domain y `NotificationUnitOfWork`.
- `Migrations/`: migracion inicial, designer y snapshot; no se utiliza `EnsureCreated`.
- `Infrastructure/Security/`: cifrado autenticado AES-GCM para tokens push.

Tablas y columnas en snake_case, siguiendo el reporte. Fechas en UTC `datetime(6)`; UUID como `varchar(36)`
con conversion explicita. El contenido es un value object owned y se almacena en columnas de la tabla de su agregado.

## Modelo y restricciones

| Tabla | Funcion | Restriccion principal |
| --- | --- | --- |
| notification_notifications | Bandeja durable y ReadAt | UNIQUE organizacion + evento + tipo + destinatario + subject_id |
| notification_announcements | Comunicados administrativos | UNIQUE organizacion + request_id |
| notification_push_subscriptions | Dispositivos, token cifrado y habilitacion | UNIQUE organizacion + instalacion; UNIQUE token_hash global |
| notification_processed_events | Inbox de eventos confirmados | UNIQUE consumer + message_id |
| notification_dispatch_jobs | Trabajo pendiente de expandir a canales | UNIQUE organizacion + notification_id |
| notification_deliveries | Entrega por canal/destino, intentos, reintentos y resultado | UNIQUE organizacion + notification_id + canal + destino |

`subject_id` es obligatorio: usa el alumno o Guid.Empty cuando no lo hay. Un CHECK exige que corresponda
al `student_id`, evitando duplicados que MySQL permite con columnas NULL en indices unicos.
Las claves foraneas compuestas incluyen organizacion: un comunicado, trabajo o entrega no puede apuntar a
un aviso de otra organizacion. No hay FK hacia tablas de otros servicios.

Los avisos, trabajos y entregas no se eliminan al deshabilitar una suscripcion. Los repositorios filtran
organizacion y usuario; la bandeja aplica los vinculos de alumnos autorizados antes de paginar. La resolucion
de esos vinculos sigue perteneciendo a Roster, no a estas tablas.

El hash del token es globalmente unico para impedir que un token activo quede asociado a dos usuarios u
organizaciones. Una instalacion tampoco se reasigna automaticamente a otra cuenta: el flujo autenticado
de registro, cierre de sesion y cambio de propietario se implementara en la fase de API/identidad.

## Transacciones e idempotencia

Los repositorios solo consultan o agregan al ChangeTracker: nunca confirman por separado.
El decorador Transaction existente llama a IUnitOfWork una vez cuando el caso de uso termina con exito.
NotificationUnitOfWork ejecuta un unico SaveChanges; EF/MySQL confirma o revierte el lote completo.

ReceiveTripEventHandler consulta el Inbox antes de resolver destinatarios y registra la marca de procesado
en el mismo lote que los avisos y el trabajo pendiente. Guarda una huella SHA-256 del payload concreto y
sus metadatos, no el payload sensible. Un reintento identico no vuelve a consultar Roster; una identidad con
contenido distinto devuelve NOTIFICATION_SOURCE_CONFLICT. Los eventos omitidos por politica tambien
quedan registrados. Los errores de validacion, autorizacion o directorio no marcan el evento como procesado.

La huella preserva el orden de las colecciones del contrato: los reintentos deben conservar el payload y
los metadatos originales. El productor no debe regenerar messageId, occurredAt o correlationId al reintentar.

Un indice unico resuelve las carreras concurrentes. La unidad de trabajo propaga el codigo
NOTIFICATION_DUPLICATE_KEY en lugar de fingir exito. El consumidor de fase 3 descarta ese scope para
reintentar en uno nuevo y consultar el Inbox antes de hacer ACK. La concurrencia optimista de avisos,
suscripciones, trabajos y entregas utiliza un contador version; un conflicto produce NOTIFICATION_CONCURRENT_UPDATE.

INotificationDispatchQueue solo persiste un trabajo. El worker de fase 4 crea las entregas por canal y
marca el trabajo como expandido en una transaccion; despues procesa los envios fuera de esa transaccion.
Versiones e indices impiden que dos expansiones confirmen el mismo trabajo. No se promete push exactamente una vez.
La fase 3 incorpora el consumidor RabbitMQ y la fase 4 agrega FCM, SignalR y el worker (ver README del servicio).
No hay cambios de esquema en fase 4: se utilizan las seis tablas existentes y locks de sesion MySQL para
coordinar workers. Aceptacion por el proveedor no significa entrega ni lectura; una caida entre envio y
commit puede repetir el envio externo. El indice unico mantiene un solo aviso y trabajo logico.

El contexto rechaza agregados con DomainEvents pendientes hasta que exista un Outbox de publicacion durable;
no los descarta silenciosamente. Los modelos actuales no generan esos eventos de salida.

## Secretos y cifrado

Configurar externamente:

- `ConnectionStrings__NotificationDatabase`: conexion a la base exclusiva, con usuario propio y permisos limitados.
- `NotificationPersistence__PushTokenEncryptionKey`: clave aleatoria de 32 bytes codificada en Base64.

Los tokens se guardan como `token_ciphertext` con nonce aleatorio y autenticacion AES-GCM. Un hash permite
la unicidad sin almacenar el token en claro. No se habilita SensitiveDataLogging. La clave no se guarda en
appsettings compartidos, migraciones, imagenes ni base de datos. Estos tokens no se devuelven como Resources REST.

Generar una clave UNA vez y conservarla de forma segura, por ejemplo en un gestor de secretos:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

No generar otra clave en cada arranque. Perderla impide descifrar las suscripciones existentes; cambiarla
requiere una migracion controlada de recifrado o volver a registrar los dispositivos. La rotacion multiclave
no esta implementada. Cifrado de volumen, backups, TLS y gestion de secretos de produccion deben configurarse
en la infraestructura de despliegue; el cifrado del token no equivale a cifrar toda la base.

## Base permanente de desarrollo

Desde la raiz del repositorio, con Docker funcionando:

1. Copiar `src/Services/Notification/infrastructure/.env.example` a `.env` en esa misma carpeta y cambiar ambas contrasenas.
2. Levantar solo la base de Notification:

```powershell
docker compose --env-file src/Services/Notification/infrastructure/.env -f src/Services/Notification/infrastructure/compose.yaml up -d
```

Compose crea `rutasegura_notification` y el usuario `notification_app`, publica solamente `127.0.0.1:3308`
y conserva los datos en un volumen exclusivo. Cambiar el puerto mediante NOTIFICATION_MYSQL_PORT si esta ocupado.
No usar el usuario root del contenedor para el servicio. Las variables de inicializacion de MySQL solo se
aplican cuando el volumen esta vacio; cambiar una contrasena en .env no modifica un usuario ya existente.

3. En PowerShell, establecer los secretos reales para esta sesion:

```powershell
$env:ConnectionStrings__NotificationDatabase = 'Server=127.0.0.1;Port=3308;Database=rutasegura_notification;UserID=notification_app;Password=<tu-contrasena>;SslMode=Disabled'
$env:NotificationPersistence__PushTokenEncryptionKey = '<tu-clave-Base64-guardada>'
```

SslMode=Disabled es solo para el contenedor local de desarrollo. En produccion exigir TLS y verificar el
certificado del servidor. No publicar MySQL en interfaces de red publicas.

4. Restaurar las herramientas y aplicar la migracion de manera explicita:

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Notification/src/RutaSegura.Notification.Infrastructure --startup-project src/Services/Notification/src/RutaSegura.Notification.Infrastructure --context NotificationDbContext
```

No ejecutar migraciones contra una base compartida con otro servicio. En produccion usar una identidad
de migracion separada y un script revisado; el usuario de ejecucion no debe conservar permisos DDL.

5. Iniciar el host:

```powershell
dotnet run --project src/Services/Notification/src/RutaSegura.Notification.Api
```

Api registra la persistencia real si existe la conexion; nunca migra ni abre una conexion al arrancar.
Sin conexion configurada, solo expone liveness/OpenAPI, no readiness ni funcionalidad de negocio.
Los casos de uso continuan desactivados en el host hasta integrar directorios/identidad reales de las fases
siguientes; las pruebas los ejecutan con directorios de prueba y persistencia MySQL real.

## Verificacion repetible

El SDK requerido por global.json es 10.0.401. No se cambia ese archivo para las pruebas.

```powershell
dotnet test src/Services/Notification/tests/RutaSegura.Notification.Tests -c Release
./scripts/Test-NotificationPersistence.ps1
```

El script crea un contenedor MySQL 8.0 temporal con contrasena aleatoria, puerto dinamico solo en loopback
y almacenamiento efimero. Cada prueba crea y elimina exclusivamente su propia base `notification_test_<uuid>`.
Se aplican migraciones reales, no EnsureCreated ni un sustituto InMemory/SQLite. El script retira el
contenedor al terminar, incluso ante un fallo. No usa ni borra la base permanente o los volumenes de Compose.
Resultados TRX en artifacts/test-results/notification-persistence (ignorado).

Se puede suministrar NOTIFICATION_TEST_MYSQL para un servidor LOCAL aislado con permisos para crear bases
temporales. Sin esa variable, solo se ejecutan las pruebas de modelo, cifrado y registro; las pruebas MySQL
se omiten de forma explicita. No confundir esa ejecucion con una validacion de integracion completa.

Referencias tecnicas: [proveedor oficial MySQL](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.12)
y [migraciones de EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/).
