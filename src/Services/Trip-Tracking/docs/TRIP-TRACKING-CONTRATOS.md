# Trip & Tracking — Contratos para otros bounded contexts

> Cómo deben integrarse **IAM, Administration (Roster / Route Planning / Subscription), Notification**, el API Gateway
> y los clientes (app del conductor, app del tutor, web de la empresa) con el **Trip & Tracking Service**.
> Actualizado: 23/10/2026. Implementación y pruebas: [`TRIP-TRACKING-RESUMEN.md`](TRIP-TRACKING-RESUMEN.md).

## 0. Reglas de integración

1. **Ningún servicio lee la base de datos de Trip & Tracking.** Se integra solo por REST síncrono, eventos de RabbitMQ
   o el hub SignalR (Database per Service, PT-02).
2. **Hacia Trip & Tracking:** otros contextos publican eventos (rutas, vínculos tutor-alumno); este servicio guarda réplicas.
   No existe un endpoint REST para escribir esas réplicas.
3. **Desde Trip & Tracking:** todo lo que ocurre en un viaje se publica como evento de integración versionado.
   Notification y Administration reaccionan a esos eventos; no consultan la API para enterarse de cambios.
4. Los comandos de Application (`StartTripCommand`, etc.) son **internos**: se invocan a través de la API o de los
   consumidores. Otros servicios nunca referencian ensamblados de Trip & Tracking.

```
 App conductor ─REST─┐                         ┌─► trip-tracking.events ─► Notification (push, avisos)
 App tutor ─REST/WS──┤                         │                        └► Administration (reportes)
 Web empresa ─REST───┼─► API Gateway ─► Trip & Tracking ─┤
                     │                         └─► trip-tracking.live ─► SignalR /hubs/trip-tracking
 Administration ─ administration.events ─┐
 IAM ──────────── iam.events ────────────┴─► trip-tracking.inbound (rutas, vínculos tutor-alumno)
```

## 1. Autenticación y autorización (seguridad provista por Iam)

| Elemento | Valor |
|---|---|
| Esquema | `Authorization: Bearer <JWT>` (SignalR: `?access_token=<JWT>`) |
| Emisor del token | Solo Iam (`POST /api/v1/users/sign-in` del monolito). Trip & Tracking no emite tokens (salvo `/dev/tokens` en desarrollo sin Iam) |
| Firma | HMAC-SHA256 (`HS256`) con el secreto compartido: `Jwt:Secret` (Trip & Tracking) = `TokenSettings:Secret` (Iam), mínimo 32 bytes |
| Issuer / Audience | **Obligatorios**: `Jwt:Issuer` = `TokenSettings:Issuer` (`rutasegura-iam`), `Jwt:Audience` = `TokenSettings:Audience` (`rutasegura-api`) |
| Vigencia | `exp` obligatorio, sin tolerancia de reloj (igual que Iam); Iam emite tokens de 8 h |
| Claim de usuario | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` = Guid del usuario en Iam (ya no se acepta `sub`) |
| Claim de rol | `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`: `DRIVER`, `PARENT`, `ADMIN` (ya no se acepta `role`) |
| Claim de empresa | `organizationId`; obligatorio para `DRIVER` y `ADMIN` (como `IamPolicies.OrganizationDriver/OrganizationAdministrator`) |
| Cuenta vigente | En cada request Trip & Tracking reenvía el token a Iam (`GET {Iam:BaseUrl}/api/v1/users/me`). Iam responde 200 solo si la cuenta existe, tiene el mismo rol y organización y la organización está `ACTIVE`; además se compara id/rol/organización con el token. Caché por token: `Iam:AccountValidation:CacheDuration` (60 s) |
| Si Iam no responde | `Iam:AccountValidation:WhenUnavailable`: `Allow` (por defecto, basta el JWT validado: un SOS no depende del monolito) o `Deny` (503 `IAM_UNAVAILABLE`) |

Respuestas de seguridad (ProblemDetails con `code`):

| HTTP | `code` | Cuándo |
|---|---|---|
| 401 | `UNAUTHORIZED` | Sin token |
| 401 | `INVALID_TOKEN` | Firma, emisor, audiencia, algoritmo o formato que no son de Iam |
| 401 | `TOKEN_EXPIRED` | Token vencido: volver a iniciar sesión en Iam |
| 403 | `FORBIDDEN` | El rol o la organización del token no permiten la operación |
| 403 | `IAM_ACCOUNT_REJECTED` | Iam ya no reconoce la cuenta (eliminada, rol/organización cambiados, organización suspendida) |
| 503 | `IAM_UNAVAILABLE` | Iam caído y `WhenUnavailable=Deny` (cabecera `Retry-After`) |

**Correspondencia de identidades:** el `DriverId` de las rutas = Id de usuario `DRIVER` en IAM; el `GuardianId` de los
vínculos = Id de usuario `PARENT`; el `organizationId` del token = `OrganizationId` de las rutas.

## 2. API REST — `/api/v1`

Base local: `http://localhost:5180`. En el Gateway (YARP) se enruta `/api/v1/trips/**`, `/api/v1/tracking/**`,
`/api/v1/drivers/**`, `/api/v1/guardians/**` y `/hubs/trip-tracking/**` a este servicio.
JSON en camelCase; fechas ISO 8601 con zona (`2026-10-23T06:40:00-05:00`); enums como texto.

### 2.1 Conductor (`DRIVER`)

| Método | Ruta | Body | Respuesta |
|---|---|---|---|
| GET | `/drivers/me/trips?from&to` | — (sin rango = hoy en Lima) | `DriverTripDto[]` (incluye `routeId` para cruzarlo con `GET /api/v1/stakeholders/drivers/me/students`) |
| POST | `/trips/{tripId}/start` | `{ clientEventId }` | `DriverActionResult` |
| POST | `/tracking/positions` | `{ tripId, readings: [{ latitude, longitude, recordedAt, accuracyMeters, speedKmh?, headingDegrees? }] }` (≤ 500) | `{ accepted, ignored, rejected, lastAcceptedAt }` |
| POST | `/trips/{tripId}/boardings` | `{ studentId, clientEventId, occurredAt?, position? }` | `DriverActionResult` |
| POST | `/trips/{tripId}/alightings` | `{ studentId, clientEventId, occurredAt?, position? }` | `DriverActionResult` |
| POST | `/trips/{tripId}/absences` | `{ studentId, clientEventId, occurredAt? }` | `DriverActionResult` |
| POST | `/trips/{tripId}/passengers/{studentId}/undo` | `{ clientEventId }` | `DriverActionResult` |
| POST | `/trips/{tripId}/incidents` | `{ clientEventId, incidentType, detail?, notifyGuardians, occurredAt?, position? }` | `DriverActionResult` |
| POST | `/trips/{tripId}/sos` | `{ clientEventId, occurredAt?, position? }` | `DriverActionResult` |
| POST | `/trips/{tripId}/sync` | `{ events: [{ clientEventId, type, occurredAt, studentId?, position?, incidentType?, detail?, notifyGuardians }] }` (≤ 200) | `{ outcomes: [{ clientEventId, status, errorCode }], applied, duplicates, rejected }` |
| POST | `/trips/{tripId}/complete` | `{ clientEventId }` | `DriverActionResult` |

- `position` = `{ latitude, longitude }`. `DriverActionResult` = `{ tripId, applied, tripStatus }`.
- `clientEventId`: UUID generado en el celular por cada acción. Reenviar el mismo es seguro (`applied: false`).
- `incidentType`: `HeavyTraffic`, `RouteDeviation`, `VehicleFailure`, `StudentUnwell`, `Accident`, `Other`
  (también `HEAVY_TRAFFIC`, etc.).
- `type` de sync: `Boarding`, `Alighting`, `Absent`, `Undo`, `Incident`, `Sos`; `status`: `Applied`, `Duplicate`, `Rejected`.
- Si `/tracking/positions` responde 422 `TRACKING_REQUIRES_ACTIVE_TRIP`, la app debe dejar de compartir ubicación.

### 2.2 Tutor (`PARENT`)

| Método | Ruta | Body | Respuesta |
|---|---|---|---|
| GET | `/guardians/me/trips?from&to` | — | `GuardianTripDto[]` = `{ tripId, routeName, direction, status, scheduledStart, startedAt, isLive, myStudents[] }` |
| GET | `/trips/{tripId}/live` | — | `TripLiveStatusDto` (solo sus hijos y su parada) |
| GET | `/trips/{tripId}/position` | — | `TripPositionDto` (respaldo del hub) |
| POST | `/trips/{tripId}/guardian-absences` | `{ studentId, note? }` (≤ 250) | 204 |

### 2.3 Empresa (`ADMIN`, acotado a su `organizationId`)

| Método | Ruta | Body | Respuesta |
|---|---|---|---|
| GET | `/trips?from&to&status` | — (rango ≤ 31 días; `status` opcional) | `OrganizationTripDto[]` |
| POST | `/trips` | `{ routeId, serviceDate: "2026-10-24" }` | 201 `{ tripId }` |
| POST | `/trips/{tripId}/cancel` | `{ reason }` (solo antes de iniciar) | 204 |
| POST | `/trips/{tripId}/sos/resolve` | — | 204 |
| GET | `/trips/{tripId}/live` · `/position` · `/summary` · `/track` | — | Estado, posición, resumen, recorrido GPS |

### 2.4 Consultas compartidas

| Ruta | Roles | DTO |
|---|---|---|
| `/trips/{tripId}/live` | DRIVER (su viaje), PARENT (sus hijos), ADMIN (su empresa) | `{ tripId, routeName, direction, status, scheduledStart, startedAt, position?, nextStop?, studentsOnBoard, totalStudents, hasActiveSos, passengers[], stops[] }` |
| `/trips/{tripId}/position` | los tres | `{ tripId, status, position?, nextStopId, nextStopDistanceMeters, nextStopEtaMinutes }` |
| `/trips/{tripId}/summary` | DRIVER, ADMIN | `{ tripId, routeName, direction, status, startedAt, completedAt, delivered, absent[], incidents[] }` |
| `/trips/{tripId}/track` | DRIVER, ADMIN | `{ tripId, pointCount, points: [{ latitude, longitude, recordedAt, accuracyMeters, speedKmh, headingDegrees }] }` |

`status` del viaje: `Scheduled`, `InProgress`, `Delayed`, `Completed`, `Cancelled`.
Estado del pasajero: `Pending`, `Boarded`, `Alighted`, `Absent`. `position` es `null` si el viaje no está en curso.

### 2.5 Errores (RFC 9457)
```json
{ "type": "https://httpstatuses.io/422", "title": "Regla de negocio incumplida", "status": 422,
  "detail": "Faltan registrar 2 alumno(s).", "code": "TRIP_HAS_PENDING_STUDENTS", "traceId": "00-..." }
```

| HTTP | Códigos | Acción del cliente |
|---|---|---|
| 400 | `VALIDATION_*` (`VALIDATION_CLIENT_EVENT_ID`, `VALIDATION_INCIDENT_TYPE`, `VALIDATION_BATCH_TOO_LARGE`, `VALIDATION_DATE_RANGE`…) | Corregir la petición |
| 401 | — | Renovar el token en IAM |
| 403 | `TRIP_ACCESS_DENIED`, `TRIP_DRIVER_MISMATCH`, `ORGANIZATION_REQUIRED` | No reintentar |
| 404 | `TRIP_NOT_FOUND`, `ROUTE_NOT_FOUND` | — |
| 409 | `CONCURRENCY_CONFLICT`, `DUPLICATE_RECORD` | Reintentar con el **mismo** `clientEventId` |
| 422 | `TRIP_START_TOO_EARLY`, `TRIP_INVALID_TRANSITION`, `TRIP_NOT_ACTIVE`, `TRIP_HAS_PENDING_STUDENTS`, `TRIP_HAS_STUDENTS_ON_BOARD`, `TRIP_HAS_ACTIVE_SOS`, `TRIP_NO_ACTIVE_SOS`, `STUDENT_NOT_IN_ROSTER`, `PASSENGER_INVALID_TRANSITION`, `EVENT_TIMESTAMP_INVALID`, `INCIDENT_DETAIL_REQUIRED`, `INCIDENT_DETAIL_TOO_LONG`, `TRACKING_REQUIRES_ACTIVE_TRIP`, `ROUTE_NOT_OPERATING` | Mostrar el mensaje; no reintentar igual |
| 429 | — | Esperar (límite de telemetría) |

### 2.6 Salud
`GET /health/live` (proceso) · `GET /health/ready` (MySQL + RabbitMQ; Redis como Degraded). OpenAPI: `/openapi/v1.json`.

## 3. Tiempo real — SignalR `/hubs/trip-tracking`

| Dirección | Método | Datos |
|---|---|---|
| Cliente → servidor | `SubscribeToTrip(tripId)` | Devuelve `TripPositionDto` (última posición). Mismas reglas de acceso que `/live`; error `HubException` con el código (`TRIP_ACCESS_DENIED`, `TRIP_NOT_FOUND`) |
| Cliente → servidor | `UnsubscribeFromTrip(tripId)` | — |
| Servidor → cliente | `PositionUpdated` | `{ tripId, routeId, vehicleId, status, latitude, longitude, recordedAt, speedKmh, headingDegrees, nextStopId, nextStopDistanceMeters, nextStopEtaMinutes, studentsOnBoard }` (sin datos de alumnos) |
| Servidor → cliente | `TripStatusChanged` | `{ tripId, status, reason, occurredAt }` (inicio, retraso, fin, cancelación) |

Conexión: `/hubs/trip-tracking?access_token=<JWT>`, protocolo JSON. Si el WebSocket cae, usar `GET /trips/{id}/position`.

## 4. Eventos publicados (Trip & Tracking → otros contextos)

**Broker:** RabbitMQ · **Exchange:** `trip-tracking.events` (topic, durable) · **Routing key = tipo de mensaje** ·
mensajes persistentes, publicados vía Transactional Outbox (al menos una vez).

| Propiedad AMQP / cabecera | Valor |
|---|---|
| `message_id` | Id único del evento (= `EventId` del dominio). **Usarlo para deduplicar.** |
| `type` / cabecera `message-type` | p. ej. `trip-tracking.student-boarded` |
| cabecera `schema-version` | `1` |
| cabecera `occurred-at` | Instante de negocio ISO 8601 |
| `correlation_id` / cabecera `correlation-id` | Traza de la petición que originó el evento |
| body | JSON camelCase del payload |

Para suscribirse: declarar una cola durable propia y enlazarla a `trip-tracking.events` con el patrón deseado
(`trip-tracking.#`, `trip-tracking.sos-*`, `trip-tracking.student-*`…). Los mensajes publicados antes de existir
la cola quedan en `trip-tracking.events.unrouted`.

Todos los payloads incluyen `tripId`, `routeId` y `organizationId` (null si la ruta no tiene empresa).

| Tipo (routing key) | Payload (además de tripId, routeId, organizationId) | Consumidor sugerido |
|---|---|---|
| `trip-tracking.trip-scheduled` | `routeName, direction, driverId, vehicleId, scheduledStart` | Administration |
| `trip-tracking.trip-started` | `routeName, direction, driverId, vehicleId, expectedStudents[], startedAt` | Notification ("el bus salió") |
| `trip-tracking.trip-delayed` | `routeName, reason, affectedStudents[], occurredAt` | Notification |
| `trip-tracking.student-boarded` | `routeName, studentId, stopId, position?, occurredAt` | Notification (US-07 aviso de abordaje) |
| `trip-tracking.student-alighted` | `routeName, studentId, stopId, position?, occurredAt` | Notification |
| `trip-tracking.student-marked-absent` | `routeName, studentId, source (Driver/Guardian), note?, occurredAt` | Notification, Administration |
| `trip-tracking.passenger-record-undone` | `studentId, previousStatus, occurredAt` | Notification (corregir aviso) |
| `trip-tracking.vehicle-approaching-stop` | `routeName, stopId, stopName, distanceMeters, etaMinutes, studentsAtStop[], occurredAt` | Notification ("el bus se acerca") |
| `trip-tracking.stop-reached` | `stopId, sequence, occurredAt` | Administration |
| `trip-tracking.incident-reported` | `routeName, incidentId, type, detail?, notifyGuardians, position?, affectedStudents[], occurredAt` | Notification (si `notifyGuardians`), Administration |
| `trip-tracking.sos-raised` | `routeName, driverId, vehicleId, position?, studentsOnBoard[], occurredAt` | Notification (prioridad máxima) |
| `trip-tracking.sos-resolved` | `resolvedBy, occurredAt` | Notification |
| `trip-tracking.trip-completed` | `routeName, deliveredStudents[], absentStudents[], incidentCount, startedAt, completedAt` | Notification, Administration (reportes) |
| `trip-tracking.trip-cancelled` | `reason, occurredAt` | Notification |

`position` = `{ latitude, longitude }`. Los destinatarios (tutores de cada `studentId`, admins de `organizationId`)
los resuelve Notification con sus propios datos.

**Reglas para consumidores:** deduplicar por `message_id` (Inbox / tabla de procesados); confirmar (ACK) después de
persistir; ordenar por `occurredAt` si importa la secuencia; ignorar campos desconocidos. Cambios incompatibles
se publicarán con `schema-version` 2.

## 5. Eventos consumidos (otros contextos → Trip & Tracking)

**Cola:** `trip-tracking.inbound` (durable, DLQ `trip-tracking.inbound.dlq`). Se reconocen por el **sufijo del tipo**,
así que el prefijo puede ser `administration.` o `iam.`.

**Requisitos del publicador:** exchange topic durable (`administration.events`, `stakeholder.events` o `iam.events`), routing key = tipo,
`message_id` = UUID único y estable en reintentos, `type` = tipo de mensaje, body JSON camelCase.

### 5.1 `*.route-published` / `*.route-updated` — Administration (Route Planning)
```json
{
  "routeId": "44444444-4444-4444-4444-444444444444",
  "organizationId": "0a000000-0000-0000-0000-000000000001",
  "name": "Ruta 3",
  "direction": "Pickup",
  "driverId": "11111111-1111-1111-1111-111111111111",
  "vehicleId": "33333333-3333-3333-3333-333333333333",
  "departureTime": "06:40",
  "timeZoneId": "America/Lima",
  "operatingDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
  "stops": [ { "stopId": "a0000000-...-0001", "sequence": 1, "name": "Av. Primavera 1240", "latitude": -12.1, "longitude": -77.0 } ],
  "students": [ { "studentId": "b0000000-...-0001", "stopId": "a0000000-...-0001" } ]
}
```
Snapshot **completo** (no parcial). `direction`: `Pickup` | `Return`. Todos los alumnos deben estar en una parada de la
ruta y la zona horaria debe existir; si no, el mensaje va a la DLQ. Desde el 25/10/2026 los viajes **todavía programados** (`Scheduled`, hoy y mañana) se actualizan con la planificación nueva (paradas, alumnos, conductor, vehículo y nombre) — p. ej. un niño inscrito por su padre aparece en el siguiente viaje; los alumnos que siguen conservan su estado (una ausencia ya avisada). Los viajes en curso o terminados conservan su copia. Un cambio de hora de salida no mueve el viaje ya programado.

### 5.2 `*.route-retired` — Administration
`{ "routeId": "..." }` → deja de programar viajes de esa ruta.

### 5.3 `*.guardian-student-linked` / `*.guardian-student-unlinked` — Stakeholder (US-02)

Lo publica el bounded context **Stakeholder** del monolito en `stakeholder.events` (`stakeholder.guardian-student-linked|unlinked`) cuando un padre registra o borra a su hijo. Se mantienen los bindings a `administration.events` e `iam.events` por compatibilidad.
`{ "guardianId": "...", "studentId": "..." }` → habilita o retira el acceso del tutor al seguimiento del alumno.

| Resultado del consumo | Acción |
|---|---|
| Procesado o duplicado (`message_id` ya visto) | ACK |
| Tipo que no interesa | ACK (se descarta) |
| Sin `message_id`, JSON inválido o datos inválidos | NACK → DLQ |
| Error transitorio (BD caída) | Reintento una vez; si vuelve a fallar → DLQ |

## 6. Comandos y consultas del bounded context (Application)

Se ejecutan con `IDispatcher` (Logging → Validation → Transaction). Columna "Entrada" = cómo se dispara desde fuera.

| Comando | Entrada | Resultado / eventos emitidos |
|---|---|---|
| `ScheduleTripCommand(routeId, serviceDate, organizationId?)` | `POST /trips`, `TripSchedulingWorker` | `tripId` · `trip-scheduled` |
| `StartTripCommand` | `POST /trips/{id}/start` | `trip-started` (+ `trip-delayed`) |
| `RecordPositionsCommand` | `POST /tracking/positions` | posición en Redis + `PositionUpdated`; `vehicle-approaching-stop`, `stop-reached` |
| `RecordBoardingCommand` / `RecordAlightingCommand` | `POST /boardings` / `/alightings` | `student-boarded` / `student-alighted` |
| `MarkStudentAbsentCommand` | `POST /absences` | `student-marked-absent` (source Driver) |
| `UndoPassengerRecordCommand` | `POST /passengers/{id}/undo` | `passenger-record-undone` |
| `NotifyAbsenceCommand` | `POST /guardian-absences` | `student-marked-absent` (source Guardian) |
| `ReportIncidentCommand` | `POST /incidents` | `incident-reported` (+ `trip-delayed`) |
| `RaiseSosCommand` / `ResolveSosCommand` | `POST /sos` / `/sos/resolve` | `sos-raised` / `sos-resolved` |
| `CompleteTripCommand` | `POST /complete` | `trip-completed` + `TripStatusChanged` |
| `CancelTripCommand` | `POST /cancel` | `trip-cancelled` |
| `SyncOfflineEventsCommand` | `POST /sync` | los eventos de cada acción aplicada |
| `UpsertRouteSnapshotCommand` / `RemoveRouteSnapshotCommand` | `*.route-published|updated` / `*.route-retired` | réplica `route_snapshots` |
| `LinkGuardianStudentCommand` / `UnlinkGuardianStudentCommand` | `*.guardian-student-linked|unlinked` | réplica `guardian_students` |

| Consulta | Ruta |
|---|---|
| `GetDriverAgendaQuery` | `GET /drivers/me/trips` |
| `GetGuardianTripsQuery` | `GET /guardians/me/trips` |
| `GetOrganizationTripsQuery` | `GET /trips` |
| `GetTripLiveStatusQuery` | `GET /trips/{id}/live` |
| `GetTripPositionQuery` | `GET /trips/{id}/position`, `SubscribeToTrip` |
| `GetTripSummaryQuery` | `GET /trips/{id}/summary` |
| `GetTripTrackQuery` | `GET /trips/{id}/track` |

## 7. Resumen por equipo

| Equipo / servicio | Qué debe hacer |
|---|---|
| **IAM** | Firmar el JWT con el secreto compartido e incluir `nameidentifier`, `role` (`DRIVER`/`PARENT`/`ADMIN`) y `organizationId`. Si gestiona vínculos tutor-alumno, publicar `iam.guardian-student-linked|unlinked` en `iam.events`. |
| **Administration** | Publicar `administration.route-published|updated|retired` (snapshot completo) y, si Roster gestiona los vínculos, `administration.guardian-student-linked|unlinked` en `administration.events`. Consumir `trip-tracking.trip-completed`, `incident-reported`, `stop-reached` para reportes. |
| **Notification** | Crear su cola enlazada a `trip-tracking.events` (`trip-tracking.#`), deduplicar por `message_id` y enviar push según el tipo (abordaje, cercanía, incidente con `notifyGuardians`, SOS con prioridad). |
| **API Gateway** | Enrutar `/api/v1/trips/**`, `/api/v1/tracking/**`, `/api/v1/drivers/**`, `/api/v1/guardians/**` y `/hubs/trip-tracking/**` (con WebSockets) a este servicio. |
| **Apps** | Generar un `clientEventId` por acción, reintentar ante 409 o errores de red con el mismo id, usar `/sync` para la cola offline y el hub para el mapa en vivo. |
