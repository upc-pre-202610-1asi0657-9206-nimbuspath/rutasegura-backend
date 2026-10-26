# Controllers REST

Implementados: NotificationsController (bandeja/detalle/lectura y pagina con cursor), AnnouncementsController
(publicacion de comunicados), PushSubscriptionsController (listar/registrar/actualizar/desactivar)
y NotificationDeliveriesController (fallos y reproceso administrativo auditado).
Usan Resources y Transform de Interfaces.Rest y los CommandServices / QueryServices de Application.
No devuelven agregados ni acceden directamente al DbContext, broker o proveedores.

Api registra este assembly cuando NotificationApi:Enabled=true. JWT, SignalR, arranque y configuracion
permanecen en Api; los adaptadores FCM y el worker viven en Infrastructure. Dependencias y limites en
el README principal del servicio, seccion "Fase 4: API y canales".
