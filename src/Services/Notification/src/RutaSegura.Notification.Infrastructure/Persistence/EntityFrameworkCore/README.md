# Persistencia propia de Notification (fase 2, pendiente)

Esta carpeta reserva la convencion de persistencia; no implementa EF Core ni una conexion todavia.

Estructura prevista:
- Configuration/NotificationDbContext.cs: contexto exclusivo de este microservicio y su base MySQL.
- Configuration/Extensions/ModelBuilderExtensions.cs: ApplyNotificationConfiguration, llamado por OnModelCreating.
- Repositories/NotificationRepository.cs y Repositories/AnnouncementRepository.cs: implementaciones de los contratos de Domain.
- Migrations/: migraciones propias, sin compartir tablas o contexto con IAM, Roster o Tracking.

Los contratos de repositorio ya estan en Domain/Repositories. La unidad de trabajo debe conservar
en una sola transaccion Inbox + avisos + trabajo de entrega pendiente. Los indices unicos concurrentes,
el DbContext, los paquetes EF/MySQL y las migraciones se implementaran en fase 2, no en esta reorganizacion.
