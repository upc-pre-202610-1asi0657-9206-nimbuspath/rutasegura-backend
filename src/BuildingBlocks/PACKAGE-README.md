# RutaSegura BuildingBlocks

Versión 1.0.0 para .NET 10.

- Domain: supertipos de entidades, agregados y eventos internos.
- Application: Result, CQRS, validación, decoradores e IUnitOfWork.
- Messaging: contratos versionados para mensajes de integración y puertos de publicación/consumo; sin dependencia del broker.
- Testing: dobles genéricos para pruebas; no se referencia desde producción.

Los servicios no comparten sus entidades o reglas de negocio mediante estos paquetes.
La implementación de RabbitMQ, Outbox/Inbox y persistencia corresponde a las etapas posteriores del Sprint 1.
