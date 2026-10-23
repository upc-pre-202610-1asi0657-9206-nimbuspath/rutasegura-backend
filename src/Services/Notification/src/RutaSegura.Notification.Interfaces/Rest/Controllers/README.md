# Controllers REST

Implementados en fase 4: NotificationsController (bandeja/detalle/lectura), AnnouncementsController
(publicacion de comunicados) y PushSubscriptionsController (listar/registrar/actualizar/desactivar).
Usan Resources y Transform de Interfaces.Rest e INotificationCommandService / INotificationQueryService.
No devuelven agregados ni acceden directamente al DbContext, broker o proveedores.

Api registra este assembly cuando NotificationApi:Enabled=true. JWT, SignalR, arranque y configuracion
permanecen en Api; los adaptadores FCM y el worker viven en Infrastructure. Dependencias y limites en
el README principal del servicio, seccion "Fase 4: API y canales".
