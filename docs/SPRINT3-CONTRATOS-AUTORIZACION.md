# Contratos y autorización para la integración del Sprint 3

Implementación local del 15/11/2026. Backend en .NET 10, DDD y capas existentes. No se modificaron la web, la app móvil, Google Docs ni el despliegue AWS. No se ejecutaron comandos Git.

## Flujo de familias

El administrador puede crear una cuenta PARENT mediante IAM y registrar su contacto y alumno en su propia operación. Se conserva el autorregistro y la captura de hijos por el padre. Son entradas compatibles al padrón, no autorizaciones automáticas de seguimiento.

Un vínculo por operación, tutor y alumno pasa por PENDING, VERIFIED o REVOKED. Registrar o inscribir un alumno crea una solicitud pendiente; solo ADMIN de esa operación puede verificarla o revocarla con una referencia de revisión o motivo. Se guardan revisor, fecha y versión; la revisión y su evento Outbox se confirman en la misma transacción. Registrar al tutor en la operación tampoco verifica por sí solo su relación con el menor.

El modelo actual conserva un tutor propietario del registro del alumno. Esta entrega verifica ese vínculo existente; no introduce cotutores, transferencias de custodia ni un padrón universal de menores. La verificación administrativa se realiza según la política del equipo; el texto Reference deja una referencia de esa revisión, no valida documentos externos automáticamente.

Cancelar una inscripción libera el servicio y deja de seleccionar al tutor para avisos nuevos de esa ruta. Revocar el vínculo bloquea el acceso a datos del menor en esa operación, aunque la inscripción continúe. Son acciones diferentes: terminar el servicio no elimina automáticamente la autorización para consultar el historial permitido. Eliminar al alumno o revocar la cuenta también impide su autorización.

## Contratos públicos para las siguientes entregas web

Todos los paths llevan /api/v1 y pasan por el Gateway. ADMIN se limita a la operación del JWT; el cliente no elige una empresa en el cuerpo.

| Método y path | Rol | Entrada y resultado |
|---|---|---|
| POST /users/parents | ADMIN | firstName, lastName, email, password. Devuelve 201 con la cuenta PARENT global, sin adjudicarle una empresa como tenant único. |
| POST /stakeholders/administration/guardians | ADMIN | guardianUserId, phoneNumber, documentNumber opcional. Registra una cuenta PARENT existente en la operación; 200. |
| GET /stakeholders/administration/guardians | ADMIN | Contactos registrados y solicitantes asociados a su padrón: identificador, nombre, correo y contacto disponible. |
| POST /stakeholders/administration/students | ADMIN | guardianUserId y student con el mismo cuerpo de registro de alumno existente: nombres, fecha/colegio/grado opcionales y pickupLocation. 201 con studentId y guardianUserId; crea vínculo PENDING. |
| GET /stakeholders/administration/students | ADMIN | Padrón de su operación, sin inscripciones ni información de empresas ajenas. |
| GET /stakeholders/administration/guardian-links | ADMIN | Estado, revisión y versión de los vínculos de su operación. |
| POST /stakeholders/administration/guardian-links/{guardianId}/{studentId}/verify | ADMIN | { "reference": "Referencia de la revisión" }; 200. No permite aprobar un alumno ajeno a su padrón. |
| POST /stakeholders/administration/guardian-links/{guardianId}/{studentId}/revoke | ADMIN | { "reference": "Motivo de revocación" }; 200. No cancela la inscripción. |
| GET /stakeholders/me/guardian-links | PARENT | Sus propios estados de revisión; no devuelve notas internas ni datos de otras familias. |
| POST /routes/{routeId}/itinerary | ADMIN | Sin cuerpo: calcula y guarda el itinerario de las paradas actuales; 200 con geometry GeoJSON, distanceMeters, durationSeconds y calculatedAt. |
| GET /routes/{routeId} | ADMIN | Conserva sus campos e incorpora itinerary nullable con esos datos. |
| GET /notifications/contexts | Cuenta vigente | Devuelve organizationIds autorizados. Sirve también al PARENT cuyo JWT no tiene organizationId. |

El alta de cuenta y el perfil son operaciones separadas: si la cuenta se creó y el perfil falló, la web debe retomar el perfil con el id recibido, sin crear cuentas duplicadas. La asignación manual de alumnos a paradas ahora exige que el alumno pertenezca al padrón de esa operación; un GUID inventado o de otra empresa se rechaza.

En Notifications, para un PARENT sin tenant firmado una sola operación se selecciona automáticamente. Si tiene varias, REST usa X-Organization-Id y el hub puede usar organizationId en la query. Esa selección siempre se comprueba en el directorio; cambiar el valor no concede acceso. Los tokens anteriores con operación firmada conservan ese contexto; seleccionar otra requiere la misma verificación. No fabricar organizationId ni modificar JWT desde el navegador. La cuenta de IAM se revalida con su organización original, separada del contexto autorizado de Notifications.

## Directorios internos y límites entre contextos

Los contratos bajo /internal/v1/notifications se atienden en el monolito y no se publican por Gateway. Exigen X-Service-Token; un JWT de familia/administrador no lo reemplaza. Falta de secreto devuelve 503 y credencial incorrecta 401. Los secretos válidos son InternalServices:NotificationToken e InternalServices:TripTrackingToken (mínimo 32 caracteres). En producción deben gestionarse y rotarse fuera del repositorio.

| POST interno | Request | Respuesta |
|---|---|---|
| /recipients/students | organizationId, studentIds[] | organizationId, recipients[{organizationId,userId,studentId}]. Requiere vínculo VERIFIED, cuenta vigente e inscripción activa de esa operación. |
| /recipients/operators | organizationId | Administradores vigentes de esa empresa, studentId null. |
| /recipients/announcement | organizationId, routeId opcional | Tutores autorizados de las inscripciones activas del alcance. Una ruta ajena devuelve 404. |
| /student-links | organizationId, userId | organizationId, userId, studentIds[] con vínculo vigente. |
| /user-contexts | userId | userId, organizationIds[] autorizados. |
| /guardian-students | userId, organizationId opcional | userId, organizationId, studentIds[]. Fuente de autorización de Tracking; sin operación enumera candidatos propios. |

Stakeholder compone IAM y Route Planning mediante sus fachadas. Administration valida el padrón con un puerto y un adaptador de composición; no lee tablas de Stakeholder ni depende de su implementación. Notifications y Tracking usan HTTP autenticado; no consultan bases ajenas.

Tracking conserva las proyecciones AMQP para compatibilidad/auditoría, pero no las usa como permiso vigente. Cada consulta de un tutor comprueba la operación real del viaje. Un fallo del directorio deniega con 503; no retorna una autorización almacenada como respaldo. Los eventos linked/unlinked conservan sus campos previos y agregan organizationId y authorizationVersion cuando provienen de una revisión.

## Canales y privacidad

SubscribeToTrip y UnsubscribeFromTrip conservan nombre y argumentos. Las actualizaciones se envían a conexiones autorizadas individualmente: se revalidan cuenta, expiración y acceso al viaje antes de cada entrega, con concurrencia acotada. Un vínculo revocado deja de recibir GPS en una conexión ya abierta; no basta con haber ingresado una vez a un grupo. El hub cierra conexiones al vencer el token. Por defecto la verificación IAM de Tracking ya no conserva respuestas positivas; un override de CacheDuration debe documentar su ventana de revocación.

/live y /position minimizan pasajeros, contadores y siguiente parada para el tutor. Redis puede aportar GPS más reciente, pero no el domicilio/parada de otra familia. Fuera de un viaje activo no se devuelve posición ni se entrega un ping tardío. Notifications revalida también sus conexiones y destinatarios; no existe un método público para unirse a grupos elegidos por el cliente.

## OpenRouteService

Configurar Administration:Maps:ApiKey en el servidor; Compose usa ORS_API_KEY. BaseUrl por defecto https://api.openrouteservice.org/, perfil driving-car; admite driving-hgv, timeout acotado y respuestas limitadas. La clave viaja en Authorization del servidor al proveedor, nunca en la web ni en una URL. La integración envía [longitud,latitud] y transforma la respuesta GeoJSON y su resumen; véase la [documentación oficial de ORS](https://github.com/GIScience/openrouteservice/blob/main/docs/api-reference/endpoints/directions/requests-and-return-types.md).

Se requieren al menos dos paradas ordenadas. No configura ni optimiza automáticamente sus horarios. Si no hay clave, hay timeout, cuota agotada o respuesta inválida, se devuelve 503 con un código controlado. No se fabrica una ruta vial con Haversine. Al cambiar o reordenar paradas se invalida itinerary; geometryVersion impide guardar un cálculo sobre una versión anterior. El ETA en vivo de Tracking sigue siendo una estimación independiente, no una promesa ni un cálculo ORS con tráfico.

## Migración y configuración

La migración AddVerifiedGuardianAccessAndRoadItineraries agrega los vínculos auditados, contactos por operación y campos del itinerario. Las relaciones anteriores de alumnos no eliminados quedan PENDING; no se aprueban silenciosamente. **Después de actualizar, ADMIN debe revisarlas antes de que los padres recuperen seguimiento y avisos.** Los datos y endpoints de operación del conductor se conservan.

Aplicar la migración del monolito antes de habilitar los nuevos flujos. Compose mantiene Database:MigrateOnStartup y conecta los tokens de directorio ya generados localmente con Initialize-NotificationLocal.ps1; no hay claves reales nuevas en código. Para Tracking, configurar GuardianAccess:BaseUrl, ServiceToken y AllowInsecureHttp solo en la red local privada. En producción usar HTTPS y red restringida. La clave ORS es opcional para arrancar, pero obligatoria para calcular un itinerario.

No se aplicaron migraciones a la base del equipo ni a AWS. La prueba MySQL usó un contenedor temporal sin volúmenes existentes y lo retiró al terminar. Las claves Firebase del equipo permanecen fuera del repositorio.

## Verificación realizada

- Solución completa: compilación correcta; 880 pruebas superadas, 70 omitidas por requisitos de infraestructura no habilitados en esa corrida.
- Migración/concurrencia: una prueba adicional superada contra MySQL 8.0 real y aislado. Verifica actualización desde el esquema anterior, permisos PENDING, modelo actualizado y rechazo de geometría obsoleta. Una de las 70 omisiones de la corrida general corresponde a esta prueba que se ejecutó aparte.
- Incluye pruebas de verificación/revocación, inscripción independiente, empresas ajenas, token interno, cambios de cuenta, contexto PARENT sin tenant y multientidad, caída de proveedor/directorio y revalidación antes de entregar GPS a una suscripción existente.
- Compose: configuración validada con config --quiet; no se reinició el entorno del equipo.
- Se fijaron parches compatibles de OpenAPI y del backplane SignalR según [OpenAPI.NET](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc) y [ASP.NET Core](https://github.com/advisories/GHSA-wr8v-9x5q-ggcm). Esto no acredita que el runtime desplegado esté actualizado: revisar imagen/runtime en el cierre de despliegue.

Para repetir las pruebas, usar dotnet test RutaSegura.slnx con las dependencias del repositorio. El test MySQL es optativo mediante RUTASEGURA_CONTRACT_MYSQL y exige localhost y una base temporal llamada rutasegura_contract_test_<id>; no acepta una base del equipo. En esta máquina se verificó con SDK 10.0.300 desde fuera de la carpeta del global.json; el repositorio pide 10.0.401. Falta alinear la instalación del SDK para los comandos habituales desde la raíz, sin haber cambiado global.json.

## Pendientes que no son código de esta entrega

Configurar y probar una clave ORS real; desplegar las migraciones/tokens y comprobar el evento real Tracking -> RabbitMQ -> Notifications en el entorno compartido; conectar las pantallas web de las entregas 3/4; validar retorno y el recorrido conjunto. Las pruebas aquí descritas no sustituyen FCM real ni el ensayo navegador/app por Gateway. Mantener visibles las pruebas de infraestructura omitidas, la advertencia de SQLite de los proyectos de prueba y el runtime/SDK pendiente en el registro compartido.
