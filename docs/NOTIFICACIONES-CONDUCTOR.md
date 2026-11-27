# Notificaciones del conductor

Implementación del 26/11/2026. Son cinco tipos de aviso operativos. No se envían al conductor push de sus propias marcaciones de abordaje, descenso, GPS ni ausencias.

| Aviso | Origen confirmado | Destinatario |
| --- | --- | --- |
| Viaje asignado | `trip-tracking.trip-scheduled` | Usuario IAM del conductor del viaje |
| Viaje cancelado | `trip-tracking.trip-cancelled` | Conductor del viaje cancelado |
| Inasistencia avisada por la familia | `trip-tracking.student-marked-absent`, `source=Guardian` | Conductor del viaje; `source=Driver` se consume sin crear aviso |
| SOS atendido por la empresa | `trip-tracking.sos-resolved` | Conductor del viaje |
| Comunicado de la empresa | POST existente `/api/v1/announcements` | Familias vigentes y conductores verificados; si hay `routeId`, solamente los vinculados a esa ruta |

El comunicado general ahora incluye a conductores verificados. El formulario web lo comunica al administrador. Los comunicados anteriores conservan sus destinatarios originales; no hay difusión retrospectiva. Cancelar sigue siendo una operación permitida antes del inicio del viaje; no se inventó una cancelación durante la conducción.

## Contratos e independencia

Trip & Tracking ya publica estos cuatro eventos desde su Outbox transaccional. Se incorporan sus bindings en la cola existente `notification.trip-events.v1` y su traducción a contratos internos versionados. No se referencia su dominio desde Notifications ni se escribe directamente en sus tablas.

`GET /internal/v1/trips/{tripId}/notification-context?organizationId=...` añade `driverUserId` (IAM, no id del perfil Roster). Mantiene `organizationId`, `tripId` e `isActive`. El controlador sigue exigiendo `X-Service-Token` y ocultando viajes de otra organización.

`POST /internal/v1/notifications/recipients/drivers` recibe `{ organizationId, driverUserId?, routeId? }` y devuelve `{ organizationId, recipients: [{ organizationId, userId, studentId: null }] }`. Administration consulta sus servicios de aplicación e IAM mediante ACL. Excluye organizaciones inactivas, perfiles no verificados, roles distintos de DRIVER y cuentas de otra empresa. Un `routeId` inexistente o ajeno retorna 404. Los errores del directorio/contexto se reintentan; no se confirman como audiencia vacía.

Notifications resuelve la identidad del conductor a partir del viaje autorizado. Guarda Inbox, aviso y despacho en la transacción existente y confirma RabbitMQ después del commit. Conserva reintentos, DLQ y unicidad por evento/destinatario. En bandeja, detalle, lectura y antes de entregar vuelve a validar el vínculo del conductor. `studentId` permanece nulo en estos avisos porque la autorización es operativa, no una autorización de tutor.

Las nuevas categorías son `TripAssigned`, `TripCancelled`, `StudentAbsent` y `SosResolved`; `Announcement` se reutiliza. Se persisten como cadenas en la columna existente, por lo que no hay migración de esquema nueva. No se añade Redis ni otra base.

## Android, seguridad y configuración

FCM conserva payload genérico sin nombres, notas, GPS ni contenido libre: únicamente `notificationId` como dato. Android usa el canal `rutasegura_updates` y un tag estable por aviso. Los avisos operativos/comunicados tienen TTL de 24 horas; proximidad y los eventos familiares restantes conservan TTL de un minuto. La prioridad de transporte alta para mensajes visibles no convierte el canal de Android en una alarma.

Flutter registra su token Android con `PUT /api/v1/push-subscriptions/{installationId}`. La instalación se conserva entre sesiones, el token se actualiza al rotar y se deshabilita la suscripción al salir. La bandeja se recupera por REST con cursor; SignalR sólo da pistas para volver a consultarla. FCM en primer plano muestra un único aviso local por id durante esa ejecución. En segundo plano Android muestra el payload de notificación; el handler de background no lo vuelve a mostrar. Tocar el aviso abre la bandeja autenticada, nunca ejecuta una acción del viaje.

Una aceptación de FCM no acredita visualización. La entrega por red/FCM es al menos una vez; la idempotencia garantiza un aviso lógico en la base, no exactamente una entrega física en todos los reinicios y fallos de transporte. Offline, la baja remota puede quedar pendiente; el contenido del push permanece genérico y la aplicación reintenta la revocación. Android puede impedir la recepción al forzar la detención de la app, quitar permisos o restringir batería.

La configuración pública `android/app/google-services.json` corresponde a `com.example.rutasegura` en `rutasegura-f2703`. No se incorpora el JSON privado de Admin al APK. AWS sigue usando su secreto existente. Si cambia el applicationId para publicar, registrar esa aplicación Firebase y reemplazar la configuración pública correspondiente.

## Despliegue y aceptación pendiente

Publicar las revisiones de **Monolith, Trip & Tracking y Notifications**, manteniendo sus tokens internos coordinados y FCM habilitado como en el entorno existente. La web sólo cambia una explicación del alcance de los comunicados. No se ejecutó un despliegue desde esta implementación. Una versión antigua de Tracking sin `driverUserId` causa reintentos y puede llevar eventos nuevos a DLQ hasta desplegar la versión compatible; revisar y recuperar errores con las herramientas existentes. Los bindings nuevos capturan eventos publicados después de activarse, sin recrear avisos de viajes antiguos.

Instalar el APK, iniciar sesión como conductor verificado y activar push en la campana. Crear un viaje, cancelar otro viaje pendiente, registrar ausencia con un tutor autorizado, levantar/resolver SOS y enviar un comunicado. Comprobar cada caso con la app abierta y en segundo plano, entrada por toque, modo sin conexión, renovación del token, cierre/cambio de cuenta y suspensión del conductor. Verificar que otro conductor y otra empresa no reciben ni consultan los avisos. Una nueva ausencia marcada por el propio conductor no debe producir un push adicional.

Resultados de pruebas y pendientes del entorno se registran al cierre en el README móvil y en `RUTASEGURA-PENDIENTES.md`. La prueba real en Android contra AWS sigue pendiente; la suite local no la sustituye.
