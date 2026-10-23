# Controllers REST

En fase 1 no se exponen endpoints de negocio: faltan los adaptadores y la autenticacion productiva.

Los futuros NotificationsController y AnnouncementsController iran aqui. Usaran Resources y Transform
de Interfaces.Rest, e INotificationCommandService / INotificationQueryService de Application.
No devolveran agregados del dominio ni accederan directamente al DbContext, broker o proveedores.
No se agrega un controller vacio que aparente una API implementada.
