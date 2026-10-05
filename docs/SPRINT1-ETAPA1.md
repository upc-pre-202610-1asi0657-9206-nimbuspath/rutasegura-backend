# Sprint 1 — Implementación de la etapa 1

Fecha: 5 de octubre de 2026. Alcance: T01, T02, T03 y T04 del Sprint Backlog 1.

## Entregables

| Tarea | Implementación | Verificación |
| --- | --- | --- |
| T01 | Solución de 27 proyectos; cuatro servicios con cuatro capas y Gateway. IAM pasa de plantilla plana a `src/RutaSegura.IAM.*`. Administration contiene Roster, Route Planning y Subscription; Notification y TripTracking.Infrastructure se incorporan. Los hosts exponen liveness. | Compilación completa y pruebas de arranque de los cinco hosts; comprobación de todas las capas registradas en la solución. |
| T02 | SDK 10.0.401 conservado; configuración común de compilación; referencias hacia el núcleo. Pruebas de dependencias entre capas/servicios y fronteras internas de Administration. | Inspección de `.csproj` y ensamblados compilados; fixtures negativos de EF/ASP.NET/RabbitMQ en el núcleo, referencias a otro servicio, dependencias de negocio desde BuildingBlocks y fugas entre módulos. |
| T03 | Domain/Application existentes conservados; resultados distinguen Unauthorized de Forbidden; errores de Result se copian y protegen frente a modificaciones externas. Messaging aporta sobre versionado y puertos genéricos. Testing aporta dobles de publicación y unidad de trabajo sin reglas del negocio. | Suites previas conservadas; pruebas de validación, conflicto, excepción de handler, fallo de persistencia, consultas sin commit, JSON, identidad de mensajes y cancelación. |
| T04 | Cuatro paquetes NuGet locales 1.0.0: Domain, Application, Messaging y Testing, con metadatos y README. Modo de consumo de paquetes separado del desarrollo con referencias a proyectos. | Script que empaqueta, restaura con caché nueva, compila y ejecuta las suites; verifica tipo `package` y procedencia del feed local en los cuatro servicios y uso de Testing desde pruebas. |

## Preservación y limpieza concreta

- Se conserva el comportamiento de Trip & Tracking y sus pruebas. Las menciones del broker en comentarios se actualizan a RabbitMQ.
- Se retiran los tres archivos fuente de `_to_delete` después de comprobar que eran una plantilla WeatherForecast sin referencias en la solución ni código de negocio único.
- Las configuraciones de IAM se trasladan a su nuevo proyecto Api; el archivo HTTP apunta a `/health/live`.
- Los cambios previos del informe, `RESUMEN-AVANCE.md`, README de Trip-Tracking y archivos generados del usuario no se revierten.
- Se añade `.gitignore`; la retirada de artefactos previamente versionados del índice corresponde a T07.

## Evidencia reproducible

```powershell
dotnet test RutaSegura.slnx -c Release --logger trx --results-directory artifacts/test-results/project-references
./scripts/Verify-BuildingBlocksPackages.ps1
./scripts/Test-Stage1Hosts.ps1 -UsePackages
```

Resultados de las suites: 165 pruebas, sin fallos ni omisiones, tanto con referencias a proyectos como en la verificación de paquetes locales.

| Suite | Casos |
| --- | --- |
| TripTracking.Domain.Tests | 87 |
| TripTracking.Application.Tests | 40 |
| BuildingBlocks.Application.Tests | 9 |
| BuildingBlocks.Foundation.Tests | 7 |
| Architecture.Tests | 17 |
| Backend.Structure.Tests | 5 |

Las 133 pruebas originales siguen incluidas. Los 32 casos adicionales verifican la infraestructura de código de esta etapa. Las pruebas de estructura usan WebApplicationFactory; el script adicional comprueba procesos Kestrel reales.

Evidencias locales generadas:

- `artifacts/test-results/project-references/*.trx`.
- `artifacts/test-results/package-consumers/*.trx`.
- `artifacts/packages/*.1.0.0.nupkg`.
- `artifacts/packages/consumption-evidence.json`.
- `artifacts/host-startup/results.json` y logs por host.

Los artefactos se regeneran y están excluidos de Git; los scripts y las pruebas sí forman parte del código fuente.

## Límite de esta entrega

Esta etapa completa estructura, fronteras y componentes técnicos compartidos. No acredita login, cuentas, conductores, vehículos, bases MySQL, Outbox/Inbox, broker operativo, Redis funcional, rutas de Gateway, HTTPS o CI ya implementados.

El sobre de Messaging es un contrato técnico, no un evento de negocio UserRegistered ya publicado. RecordingUnitOfWork y RecordingMessagePublisher son exclusivamente dobles de pruebas y no se registran en los hosts.

Las fronteras nuevas de Domain/Application contienen marcadores de módulo y los registros de aplicación; sus agregados y casos de uso se incorporarán con sus historias. La dependencia de Infrastructure está preparada sin simular adaptadores productivos. En Trip & Tracking los handlers se conservan, pero no se habilitan mientras no existan sus adaptadores reales.

El informe AV2 y el Sprint Backlog no se modifican durante la implementación.
