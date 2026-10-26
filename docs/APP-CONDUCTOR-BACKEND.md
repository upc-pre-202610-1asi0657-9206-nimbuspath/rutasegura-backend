# RutaSegura · Guía del backend para construir la App Móvil del Conductor

> **Para:** Claude Code (sesión que construirá la app móvil **solo para conductores**).
> **Fuente:** código de `rutasegura-backend` (copia `RutaSegura-develop`), revisado el **25/10/2026**: `src/ApiGateway`, `src/Monolith/{Iam, Administration, Stakeholder}` y `src/Services/Trip-Tracking`.
> **Fuera de alcance:** **Notification** (su desarrollo no está completo). La app **no** debe llamar a `/api/v1/notifications/**`, `/push-subscriptions/**` ni `/hubs/notifications/**` (el Gateway responde 503 `SERVICE_DISABLED`).
> **Prototipo de referencia:** `RutaSeguraMobilConductor.pdf` — 8 pantallas: Login, Inicio, Perfil, Viaje en curso, Registrar abordaje, Reportar incidente, SOS y Viaje finalizado.

Todo lo de este documento se verificó contra controllers, DTOs, dominio, `appsettings` y `scripts/smoke/e2e.sh`. Lo que el prototipo muestra y el backend **todavía no da** se marca con ⚠️ y está en la §11.

> **Novedades respecto del resumen anterior:**
> 1. El **API Gateway ya enruta todo**: la app usa **una sola URL base**.
> 2. Hay un nuevo contexto, **Stakeholder**, que da los **nombres, grados, fotos y teléfono del apoderado** de los alumnos.
> 3. El conductor ya puede leer **su vehículo**: `GET /roster/drivers/me/vehicles`.
> 4. La agenda trae `routeId`.

---

## 0. Resumen en 12 líneas

1. La app la usan **solo cuentas con rol `DRIVER`**. No hay registro en la app: la cuenta la crea el ADMIN de la empresa.
2. **Una sola URL base: el API Gateway** (`http://localhost:8000`; desde el emulador Android, `http://10.0.2.2:8000`). Todas las rutas empiezan con `/api/v1`.
3. **Login:** `POST /api/v1/users/sign-in` (IAM). Devuelve un JWT de **8 h, sin refresh token**.
4. **Perfil:** `GET /roster/drivers/me` · **Mis rutas:** `/roster/drivers/me/routes` · **Vehículo:** `/roster/drivers/me/vehicles` (Administration).
5. **Alumnos** con nombre, grado, foto, parada y teléfono del apoderado: `GET /stakeholders/drivers/me/students` (Stakeholder). Se guardan en caché para usarlos sin señal.
6. **Agenda del día:** `GET /drivers/me/trips` (Trip & Tracking). Los viajes los programa el backend; la app no crea viajes.
7. **Operación del viaje:** iniciar, abordajes, ausencias, deshacer, incidente, SOS y finalizar. Son `POST` a `/trips/{id}/…`, todos con un `clientEventId` (UUID generado en el celular), de modo que reintentar es seguro.
8. **GPS:** lotes a `POST /tracking/positions` cada 5–10 s, **solo mientras el viaje está en curso**.
9. **Sin señal:** cola local persistente; al reconectar se envía con `POST /trips/{id}/sync` (≤ 200 eventos) y el GPS en lotes de ≤ 500.
10. La app **no publica ni consume RabbitMQ**: cada comando hace que el backend emita eventos (alertas a la empresa, más adelante avisos a los padres).
11. **Tiempo real opcional:** SignalR `/hubs/trip-tracking`, para enterarse si la empresa cancela el viaje o resuelve el SOS.
12. Los errores llegan en **ProblemDetails (RFC 9457) con `code`**: la app decide qué hacer **por `code`**, nunca por el texto.

---

## 1. Arquitectura actual

```
                    ┌─────────────── API Gateway (YARP) :8000 ───────────────┐
 App Conductor ───► │ Una sola URL base · reenvía ruta, query, body y token  │
  (REST + WS)       │ Rate limit sign-in · X-Correlation-Id · errores propios│
                    └──────────────┬──────────────────────────────┬──────────┘
                                   │                              │
      ┌────────────── Monolito .NET 10 :5000 ──────────┐   ┌──── Trip & Tracking .NET 10 :5180 ────┐
      │ IAM            cuentas, organizaciones, JWT     │   │ Viajes, pasajeros, GPS, incidentes,  │
      │ Administration roster (conductores, vehículos), │   │ SOS, sync offline                     │
      │                rutas, paradas, alertas          │   │ SignalR /hubs/trip-tracking · Redis   │
      │ Stakeholder    padres, hijos (nombre, foto),    │   └───────────────────┬───────────────────┘
      │                inscripciones                    │                       │
      └───────────────────────┬─────────────────────────┘                       │
                              │        RabbitMQ (Outbox / Inbox, DLQ)           │
                              └── administration.events / stakeholder.events ──►│
                              ◄───────────── trip-tracking.events ──────────────┘
  MySQL 8.4: una base por proceso (rutasegura_monolith, rutasegura_trip_tracking)
```

| Componente | Local | Desde el emulador Android | Salud |
|---|---|---|---|
| **API Gateway**: la única que usa la app | `http://localhost:8000` | `http://10.0.2.2:8000` | `/health/live`, `/health/ready` |
| Monolito (IAM + Administration + Stakeholder) | `:5000` (Swagger `/swagger`) | — | `/health` |
| Trip & Tracking | `:5180` (Swagger `/swagger`) | — | `/health/live`, `/health/ready` |
| RabbitMQ / MySQL / Redis | `5672` (UI `15672`) / `3307` / `6379` | — | — |

Desde un celular físico se usa la IP de la PC en la red local (`http://192.168.x.x:8000`). Como es HTTP, hay que permitir *cleartext* solo en el *flavor* de desarrollo (Android `network_security_config`, iOS ATS).

---

## 2. API Gateway (`src/ApiGateway`)

### 2.1 Qué se hizo
- Reverse proxy **YARP**: punto de entrada único para la app del conductor, la app del tutor y la web de la empresa. Reenvía cada ruta **sin cambiarla**: ruta, query, cuerpo y `Authorization`; para SignalR, `?access_token=`.
- **No autentica ni autoriza:** eso lo hace cada servicio con la seguridad de IAM. El Gateway solo reenvía el token.
- Protección:
  - Límite de **10 intentos por minuto e IP** en sign-in y sign-up (429 `TOO_MANY_REQUESTS`).
  - Cuerpo máximo de **2 MB**.
  - CORS solo para la web; las apps móviles no usan CORS.
- Cada respuesta lleva **`X-Correlation-Id`**. Si la app lo envía, se respeta; conviene enviarlo y registrarlo en los logs de la app.
- `/health/ready` responde 200 solo si responden el monolito y Trip & Tracking.
- Pruebas: 49 en verde; prueba de humo `e2e.sh` con **75 OK y 0 FAIL**, incluido el WebSocket real al hub a través del Gateway.

### 2.2 Tabla de enrutamiento

| Ruta pública | Servicio | ¿La usa el conductor? |
|---|---|---|
| `POST /api/v1/users/sign-in` (rate limit) | Monolito · IAM | ✅ Login |
| `POST /api/v1/users` (rate limit) | Monolito · IAM (registro de padres) | ❌ |
| `/api/v1/users/**` | Monolito · IAM | ✅ `GET /users/me` |
| `/api/v1/organizations/{id}` (solo GET y PUT) | Monolito · IAM | ✅ GET (nombre de la empresa) |
| `/api/v1/roster/**`, `/api/v1/routes/**`, `/api/v1/operations/**` | Monolito · Administration | ✅ solo `/roster/drivers/me/**` |
| `/api/v1/stakeholders/**` | Monolito · Stakeholder | ✅ alumnos y fotos |
| `/api/v1/trips/**`, `/api/v1/tracking/**`, `/api/v1/drivers/**`, `/api/v1/guardians/**` | Trip & Tracking | ✅ todo el viaje (salvo `/guardians`) |
| `/hubs/trip-tracking/**` (WebSocket) | Trip & Tracking | ✅ opcional |
| `/api/v1/notifications/**`, `/announcements/**`, `/push-subscriptions/**`, `/hubs/notifications/**` | Notification (deshabilitado) | ❌ 503 `SERVICE_DISABLED` |

**No se exponen** (404 `GATEWAY_ROUTE_NOT_FOUND`): `/internal/**`, `/dev/**`, Swagger de los servicios, `/health` de los servicios y `POST /api/v1/organizations` (provisioning).

### 2.3 Errores propios del Gateway

| HTTP | `code` | Qué hace la app |
|---|---|---|
| 404 | `GATEWAY_ROUTE_NOT_FOUND` | Bug de ruta en la app: registrar y no reintentar |
| 429 | `TOO_MANY_REQUESTS` | Login: mostrar "Demasiados intentos, espera un minuto" |
| 502 | `UPSTREAM_UNAVAILABLE` | Servicio caído: encolar y reintentar con backoff |
| 503 | `SERVICE_DISABLED` | No debería ocurrir (no llamar a Notification) |
| 504 | `UPSTREAM_TIMEOUT` | Reintentar con el **mismo** `clientEventId` |

Los errores de los servicios (401 `TOKEN_EXPIRED`, 422 `TRIP_HAS_PENDING_STUDENTS`, …) **llegan sin cambios**.

---

## 3. De qué se encarga la app móvil (pantallas del prototipo → funcionalidades)

### 3.1 Responsabilidades

| Responsabilidad | Detalle |
|---|---|
| Autenticarse | Correo y contraseña; rechazar cuentas que no sean `DRIVER`; token en almacenamiento seguro; cerrar sesión. |
| Mostrar el día | "Tu próximo viaje" y "Más tarde hoy" a partir de la agenda; retomar un viaje en curso al abrir la app. |
| Perfil | Nombre, licencia (categoría, vencimiento, estado), empresa, vehículo asignado y "Mis rutas". |
| Checklist "Antes de salir" | GPS activado y precisión, datos móviles y batería. **100 % local**, no hay endpoint. |
| Iniciar viaje | Solo dentro de la ventana permitida (60 min antes). |
| Compartir ubicación | Servicio en primer plano; lotes de GPS **solo** con el viaje en `InProgress` o `Delayed`. |
| Registrar pasajeros | "Subió", "No vino", "Bajó" (en retorno) y "Deshacer". Con nombre, grado y foto; muestra el aviso de ausencia del apoderado. |
| Reportar incidente | 6 tipos, detalle opcional y la opción "Avisar a los padres de la ruta". |
| SOS | Cuenta regresiva de 5 s para cancelar **en el celular**; recién después se llama al backend, con reintento agresivo. |
| Finalizar | Validar pendientes y SOS; enviar la cola; mostrar el resumen. |
| Funcionar sin señal | Cola local persistente (Store-and-Forward), caché de agenda, `/live` y alumnos, y reintentos idempotentes. |

**La app NO hace:**
- Crear viajes, rutas, paradas, alumnos, vehículos ni conductores (eso es la web del ADMIN).
- Ver el código de inscripción de la ruta (el backend lo oculta al conductor).
- Resolver un SOS (lo cierra el ADMIN).
- Cancelar viajes.
- Recibir push (Notification).
- Recuperar la contraseña.

### 3.2 Pantalla → endpoints (todas con la URL del Gateway)

| Pantalla | Qué muestra el prototipo | Endpoints |
|---|---|---|
| **Login** | "DNI o correo", contraseña, "¿Olvidaste tu contraseña? Pide a tu empresa…" | `POST /api/v1/users/sign-in`. ⚠️ Solo por correo: cambiar la etiqueta a "Correo" |
| **Inicio** | Fecha, "Buenos días, Jorge", próximo viaje (Ruta 3, recojo, 6:40, 7 paradas, 18 alumnos, ABC-123), "Iniciar viaje", checklist, "Más tarde hoy", aviso de privacidad | `GET /drivers/me/trips` (+ `GET /roster/drivers/me` para el saludo y `GET /roster/drivers/me/vehicles` para la placa, cruzando `vehicleId`) |
| **Perfil** | Nombre, "Conductor en Transportes Santa Rosa", licencia A-IIb (vence el 12/08/2028, vigente), vehículo (placa, modelo, capacidad, SOAT), mis rutas, cerrar sesión | `GET /roster/drivers/me`, `GET /roster/drivers/me/vehicles`, `GET /roster/drivers/me/routes`, `GET /organizations/{organizationId}`. ⚠️ SOAT no existe |
| **Viaje en curso** | "En curso desde 6:41, 24 min", "Compartiendo ubicación hace 3 s", siguiente parada 4 de 7, "Suben 4 alumnos", 3 min / 850 m, "A bordo 9 de 18", botones Registrar abordaje / Incidente / SOS / Finalizar | `POST /trips/{id}/start`, `GET /trips/{id}/live`, `POST /tracking/positions` |
| **Registrar abordaje** | Parada 4 de 7 (subida), lista con iniciales y foto, nombre, grado, "Subió 6:58 · Deshacer", "No vino / Subió", "Su apoderado avisó que hoy no asiste", "Faltan registrar 3 alumnos" | `GET /trips/{id}/live` (estado) + `GET /stakeholders/drivers/me/students` (nombres) + `GET /stakeholders/students/{id}/photo`; acciones `POST /trips/{id}/boardings` · `/absences` · `/alightings` · `/passengers/{studentId}/undo` |
| **Reportar incidente** | 6 tipos, detalle, "Añadir foto", "Nota de voz", "Avisar a los padres de la ruta", "Sin señal, se envía al reconectar" | `POST /trips/{id}/incidents` (o en cola → `/sync`). ⚠️ Sin foto ni nota de voz |
| **SOS** | "Avisa de inmediato a tu empresa y a las familias de los 9 alumnos a bordo", "Tendrás 5 segundos para cancelar" | `POST /trips/{id}/sos` |
| **Viaje finalizado** | "de 6:41 a 7:53", "17 alumnos llegaron", "1 no vino: Camila Huamán (aviso del apoderado)", incidentes, "Todos los registros enviados", "Dejaste de compartir tu ubicación", siguiente viaje | `POST /trips/{id}/complete`, `GET /trips/{id}/summary` + agenda |
| (reconexión) | — | `POST /trips/{id}/sync` |

---

## 4. IAM (Monolito) — autenticación

### 4.1 Qué se hizo
- Bounded context de **cuentas, roles (RBAC), organizaciones** (empresas de transporte, que son los *tenants*) y **emisión de JWT**.
- Reglas de cuentas:
  - **ADMIN:** uno por organización, creado junto con ella mediante provisioning con `X-Provisioning-Key`, que el Gateway no expone. Más adelante lo hará Subscription.
  - **DRIVER:** lo crea el ADMIN con `POST /api/v1/users/drivers`; la organización sale del token del ADMIN.
  - **PARENT:** se registra solo. Cualquier otro rol → 403 `SelfRegistrationNotAllowed`.
- Seguridad corregida:
  - No hay registro anónimo de ADMIN ni de DRIVER.
  - `GET /users` está aislado por organización.
  - Sign-in con la organización suspendida → 403.
  - Contraseñas de 8 a 72 caracteres, con al menos una letra y un dígito.
  - Token de **8 h** con `iss`/`aud` validados; secretos fuera de `appsettings.json`.
- Da seguridad a los demás contextos:
  - `AddIamSecurity`.
  - Políticas `OrganizationDriver`, `OrganizationAdministrator`, `OrganizationStaff` y `Guardian`.
  - `ActiveIamAccountHandler`: en cada request verifica que la cuenta siga existiendo, con el mismo rol y organización, y que la organización esté `ACTIVE`.
- Trip & Tracking **solo acepta tokens de IAM** y confirma la cuenta contra `GET /users/me`, con caché de 60 s. Si IAM está caído, por defecto **permite** la operación, para que un SOS no dependa del monolito.

### 4.2 Endpoints que usa la app

| Método | Ruta | Acceso | Comando / Query | Uso |
|---|---|---|---|---|
| POST | `/api/v1/users/sign-in` | Anónimo (rate limit 10/min) | `SignInCommand` | Login |
| GET | `/api/v1/users/me` | Cualquier cuenta | `GetUserByIdQuery` | Validar la sesión al abrir la app |
| GET | `/api/v1/organizations/{id}` | Miembros de la organización | `GetOrganizationByIdQuery` | "Conductor en {empresa}" |

```http
POST {GW}/api/v1/users/sign-in
Content-Type: application/json

{ "email": "carlos@movilidadsur.pe", "password": "Password123" }
```
```json
// 200 AuthenticatedUserResource
{ "id": "6f1c…", "firstName": "Carlos", "lastName": "Ramirez", "email": "carlos@movilidadsur.pe",
  "roleTier": "DRIVER", "organizationId": "0a00…", "token": "eyJhbGciOiJIUzI1NiIs…" }
```
- **Si `roleTier != "DRIVER"`**, rechazar el ingreso ("Esta app es solo para conductores") y descartar el token.
- Errores:
  - 401 `InvalidCredentials`.
  - 403 `OrganizationNotActive`: empresa suspendida.
  - 400 de validación.
  - 429 `TOO_MANY_REQUESTS`, que pone el Gateway.
- `GET /users/me` → `{ id, firstName, lastName, email, roleTier, organizationId }`.
- `GET /organizations/{id}` → `{ id, name, status, createdAt }`.

### 4.3 El token

| Elemento | Valor |
|---|---|
| Cabecera | `Authorization: Bearer <token>`. SignalR: `?access_token=<token>` |
| Algoritmo / vigencia | HS256, **8 h**, sin tolerancia de reloj. **Sin refresh token** (pendiente TS-01) |
| `iss` / `aud` | `rutasegura-iam` / `rutasegura-api` |
| Id de usuario | claim `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` |
| Rol | claim `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` = `DRIVER` |
| Empresa | claim `organizationId` (obligatorio para DRIVER) |

**Manejo de sesión:**
- Token en Keychain/Keystore (`flutter_secure_storage`, `expo-secure-store`, EncryptedSharedPreferences).
- Decodificar `exp` y avisar antes de que venza. **No** forzar el logout en medio de un viaje: conservar la cola, pedir re-login y luego sincronizar.
- 401 `TOKEN_EXPIRED` / `INVALID_TOKEN` → ir al login sin borrar la cola.
- 403 `IAM_ACCOUNT_REJECTED` / `OrganizationNotActive` → cerrar sesión y mostrar "Comunícate con tu empresa".

---

## 5. Administration (Monolito) — lo que el conductor puede leer

### 5.1 Qué se hizo
- Es un bounded context **dentro del monolito**, con capas Domain, Application, Infrastructure e Interfaces.
- **Roster:**
  - `Driver`: nace en `PENDING_VERIFICATION`; solo un `VERIFIED` con licencia vigente puede asignarse a una ruta. Puede quedar `SUSPENDED`.
  - `Vehicle`: placa única, aforo, habilitado o deshabilitado.
- **Route Planning:**
  - `Route` + `RouteStop`: hasta 30 paradas, separadas al menos 60 m. Cada parada tiene `origin` `MANUAL` o `ENROLLMENT`; las de inscripción se crean solas a partir de la dirección de recojo.
  - Asignación de conductor + vehículo, control de aforo y conflicto de horarios (`ResourceScheduleConflict`).
  - Límite de rutas activas del plan (`PlanRouteLimitReached`).
  - Código de inscripción por ruta para las familias. **Se oculta al conductor.**
- **Operations:** consume de Trip & Tracking `incident-reported`, `sos-raised`, `sos-resolved` y `trip-completed`, y crea `OperationalAlert` (con severidad) y `TripRecord` (reportes).
- Integración por RabbitMQ con Outbox e Inbox. Al **activar** una ruta publica `administration.route-published` con el snapshot completo; Trip & Tracking lo recibe y **programa los viajes de hoy y mañana**.
- El `driverId` publicado es el **id de usuario IAM** del conductor: Trip & Tracking lo compara con el JWT.

### 5.2 Endpoints del conductor (política `OrganizationDriver`)

| Método | Ruta | Query interna | Respuesta | Uso |
|---|---|---|---|---|
| GET | `/api/v1/roster/drivers/me` | `GetDriverByUserIdQuery` | `DriverResource` | Perfil, saludo |
| GET | `/api/v1/roster/drivers/me/routes` | `GetDriverByUserIdQuery` + `GetRoutesByDriverQuery` | `RouteResource[]` (`enrollmentCode: null`) | "Mis rutas" |
| GET | `/api/v1/roster/drivers/me/vehicles` | `+ GetVehicleByIdQuery` | `DriverVehicleResource[]` | "Vehículo asignado", placa en Inicio |

Si el ADMIN todavía no registró la cuenta en el Roster, los tres responden **404 `DriverNotFound`**. Mensaje sugerido: "Tu empresa aún no completó tu registro".

```jsonc
// DriverResource
{ "id": "…(id del Roster)…", "userId": "…(id IAM)…", "firstName": "Jorge", "lastName": "Mendoza Salas",
  "documentNumber": "45781236", "licenseNumber": "Q45781236", "licenseCategory": "A-IIb",
  "licenseExpiresOn": "2028-08-12", "phoneNumber": "987654321",
  "status": "VERIFIED",                // PENDING_VERIFICATION | VERIFIED | SUSPENDED
  "verifiedAt": "…", "createdAt": "…" }

// RouteResource
{ "id": "…", "name": "Ruta 3", "direction": "PICKUP",      // PICKUP | RETURN
  "stopPurpose": "…", "status": "ACTIVE",                   // DRAFT | ACTIVE | INACTIVE
  "schedule": { "departureTime": "06:40:00", "estimatedArrivalTime": "07:50:00",
                "timeZoneId": "America/Lima", "operatingDays": ["Monday","Tuesday","Wednesday","Thursday","Friday"] },
  "assignment": { "driverId": "…(Roster)…", "vehicleId": "…", "assignedAt": "…" },
  "studentCount": 18,
  "stops": [ { "id": "…", "sequence": 1, "name": "Av. Primavera 1240", "address": "…",
               "latitude": -12.11, "longitude": -76.98, "studentIds": ["…"], "origin": "MANUAL" } ],
  "activatedAt": "…", "createdAt": "…", "enrollmentCode": null }

// DriverVehicleResource
{ "id": "…", "licensePlate": "ABC-123", "seatingCapacity": 22, "brand": "Toyota", "model": "Coaster",
  "status": "…", "routeIds": ["…"] }
```

- "Lunes a viernes" se arma con `schedule.operatingDays`. "Vigente" se calcula con `licenseExpiresOn` frente a la fecha de hoy, junto con `status == VERIFIED`.
- **Dos ids del conductor:**
  - `DriverResource.id` es el id del **Roster**; `userId` es el de **IAM** (el del token).
  - La app nunca envía ninguno de los dos: cada backend lo toma del token.
- El conductor **no** puede usar `/roster/vehicles/**`, `/routes/**` ni `/operations/**`: son solo del ADMIN (403).

---

## 6. Stakeholder (Monolito) — los alumnos que ve el conductor

### 6.1 Qué se hizo
- Bounded context nuevo:
  - El **padre (PARENT)** registra a sus hijos: nombre, fecha de nacimiento, grado, colegio, foto y dirección de recojo con punto en el mapa.
  - Inscribe a cada hijo en una ruta con el código de inscripción.
- Al inscribirse, el alumno va a la parada que esté a ≤ 150 m o a una parada `ENROLLMENT` nueva, que se inserta donde menos alarga el recorrido. La ruta actualizada viaja a Trip & Tracking con `route-updated`. Los viajes aún `Scheduled` se actualizan y los alumnos que ya estaban conservan su estado (por ejemplo, una ausencia avisada).
- **Privacidad (AC-01):** la foto de un menor **nunca es pública**. Solo la descargan, con su token, el padre, el ADMIN de la empresa y el **conductor de una de sus rutas**.
- Publica en `stakeholder.events`:
  - `stakeholder.guardian-student-linked` y `guardian-student-unlinked`, que Trip & Tracking consume para que el padre vea los viajes de su hijo.
  - `stakeholder.student-enrolled` y `student-enrollment-ended`.

### 6.2 Endpoints que usa la app

| Método | Ruta | Política | Query interna | Uso |
|---|---|---|---|---|
| GET | `/api/v1/stakeholders/drivers/me/students` | `OrganizationDriver` | `GetDriverStudentsQuery` | **Todos** los alumnos activos de todas sus rutas. Descargarlo al iniciar sesión y antes de cada viaje, y guardarlo para usarlo sin señal |
| GET | `/api/v1/stakeholders/routes/{routeId}/students` | `OrganizationStaff` (el conductor solo ve sus rutas) | `GetRouteStudentsQuery` | Alumnos de una ruta (con el `routeId` de la agenda) |
| GET | `/api/v1/stakeholders/students/{studentId}/photo` | Cuenta autenticada con permiso | `GetStudentPhotoQuery` | Foto (JPEG, PNG o WebP; `ETag`; `Cache-Control: private, max-age=300`) |

```jsonc
// RouteStudentResource (para el conductor, guardian.email = null)
{ "studentId": "…", "enrollmentId": "…", "routeId": "…", "routeName": "Ruta 3",
  "firstName": "Valeria", "lastName": "Quispe", "fullName": "Valeria Quispe",
  "schoolGrade": "3.° de primaria", "schoolName": "Colegio San Agustín",
  "photoUrl": "/api/v1/stakeholders/students/{id}/photo?v=3",          // null si no tiene foto
  "pickupLocation": { "address": "…", "reference": "…", "latitude": -12.1, "longitude": -76.9 },
  "stop": { "stopId": "…", "name": "Av. Primavera 1240", "address": "…", "sequence": 4, "latitude": -12.1, "longitude": -76.9 },
  "guardian": { "userId": "…", "fullName": "Rosa Quispe", "phoneNumber": "987…", "email": null },
  "enrollmentStatus": "ACTIVE", "enrolledAt": "…", "endedAt": null, "endReason": null }
```

**Cómo usarlo:**
- **Unir con el viaje** por `studentId` (y por `routeId` del viaje): `live.passengers[].studentId` ↔ `students[].studentId`.
- `photoUrl` es **relativa**: concatenarla con la URL del Gateway y enviar `Authorization`. No usar un cargador de imágenes que no mande cabeceras. Cachear por `?v=` (versión de la foto).
- Iniciales del avatar ("VQ") a partir de `firstName`/`lastName` cuando `photoUrl` es `null`.
- `guardian.phoneNumber` permite un botón opcional "Llamar al apoderado".
- **Plan B:** si un `studentId` del viaje no está en Stakeholder (alumnos agregados a mano por el ADMIN con `PUT /routes/{id}/stops/{stopId}/students/{studentId}`, que hoy acepta cualquier UUID), mostrar "Alumno sin ficha" con la parada.

---

## 7. Trip & Tracking — operación del viaje (núcleo de la app)

### 7.1 Qué se hizo
- Microservicio **hexagonal**: Domain, Application, Infrastructure y Api.
  - .NET 10, EF Core 10 + MySQL 8.4.
  - RabbitMQ: Outbox con *publisher confirms*, Inbox idempotente, DLQ.
  - Redis para la última posición; SignalR para el tiempo real.
- Agregado `Trip`:
  - Estados del viaje: `Scheduled → InProgress | Delayed → Completed`, o `Cancelled`.
  - Estados del pasajero: `Pending → Boarded → Alighted`, o `Absent` (con `absenceSource` `Driver` o `Guardian`).
  - Paradas con aviso de "acercándose" a 800 m y "llegada" a 60 m; incidentes; SOS.
- **Idempotencia por `clientEventId`** en todas las acciones del conductor; sincronización offline ordenada por `occurredAt`.
- `TripSchedulingWorker` crea los viajes a partir de las rutas activas que llegan de Administration (en docker-compose corre cada 10 s).
- Pruebas: 94 de dominio, 73 de aplicación, 72 de integración (MySQL y RabbitMQ reales), 48 de seguridad y 19 de integración real con IAM.

### 7.2 Convenciones
- JSON en **camelCase**; fechas en **ISO 8601 con zona** (`2026-10-26T06:40:00-05:00`); enums como **texto**.
- **Cada acción del conductor lleva `clientEventId`**: un UUID v4 **por toque**, generado y guardado **en el momento del toque** (no al enviar).
  - Reenviar la misma petición es seguro: responde 200 con `applied: false`.
  - Si falta → 400 `VALIDATION_CLIENT_EVENT_ID`.
- `occurredAt` (opcional): hora real del toque. **Enviarlo siempre**, sobre todo si la acción sale de la cola.
- `position` (opcional): `{ "latitude": -12.1, "longitude": -76.9 }`, la última lectura GPS buena.
- Respuesta estándar: `DriverActionResult = { "tripId": "…", "applied": true, "tripStatus": "InProgress" }`.
- Políticas:
  - `driver`: rol DRIVER + `organizationId`. Además, el viaje debe ser suyo; si no, 403 `TRIP_DRIVER_MISMATCH`.
  - `staff`: DRIVER o ADMIN (`/summary`, `/track`).
  - `trip-viewer`: + PARENT (`/live`, `/position` y el hub).

### 7.3 Endpoints del conductor (paso a paso)

#### a) Agenda — `GET /api/v1/drivers/me/trips?from=&to=` · `GetDriverAgendaQuery`
Sin parámetros devuelve **hoy en hora de Lima**. Excluye los cancelados y viene ordenada por hora.
```json
[ { "tripId": "…", "routeId": "…", "routeName": "Ruta 3", "direction": "Pickup",
    "scheduledStart": "2026-10-26T06:40:00-05:00", "status": "Scheduled",
    "vehicleId": "…", "stopCount": 7, "studentCount": 18 } ]
```
- `direction`: `Pickup` (recojo: casas → colegio) | `Return` (retorno: colegio → casas).
- Primer viaje no `Completed` = "Tu próximo viaje"; el resto, "Más tarde hoy".
- Placa: buscar `vehicleId` en `/roster/drivers/me/vehicles`.
- Si hay un viaje en `InProgress` o `Delayed`, abrir directamente "Viaje en curso" (recuperación después de cerrar la app).

#### b) Iniciar — `POST /api/v1/trips/{tripId}/start` · `StartTripCommand`
```json
{ "clientEventId": "3b0e…" }
```
- Emite `trip-started`, y también `trip-delayed` si arranca más de 10 min tarde (estado `Delayed`).
- Errores:
  - 422 `TRIP_START_TOO_EARLY`: más de 60 min antes de la hora.
  - 422 `TRIP_INVALID_TRANSITION`.
  - 403 `TRIP_DRIVER_MISMATCH`.
- Después del 200: arrancar el servicio de ubicación en primer plano, refrescar los alumnos de Stakeholder y cargar `/live`.
- **No** va por `/sync`: si no hay red, reintentar con el mismo `clientEventId`.

#### c) Estado en vivo — `GET /api/v1/trips/{tripId}/live` · `GetTripLiveStatusQuery`
Es la fuente de verdad de "Viaje en curso" y "Registrar abordaje".
```jsonc
{ "tripId": "…", "routeName": "Ruta 3", "direction": "Pickup", "status": "InProgress",
  "scheduledStart": "…", "startedAt": "…",
  "position": { "latitude": -12.1, "longitude": -76.9, "recordedAt": "…", "speedKmh": 22, "headingDegrees": 90, "ageSeconds": 3 },
  "nextStop": { "stopId": "…", "name": "Av. Primavera 1240", "sequence": 4, "totalStops": 7, "distanceMeters": 850, "etaMinutes": 3 },
  "studentsOnBoard": 9, "totalStudents": 18, "hasActiveSos": false,
  "passengers": [ { "studentId": "…", "stopId": "…", "status": "Pending",   // Pending | Boarded | Alighted | Absent
                    "boardedAt": null, "alightedAt": null, "absenceSource": null, "absenceNote": null } ],
  "stops": [ { "stopId": "…", "name": "…", "sequence": 1, "latitude": -12.1, "longitude": -76.9, "reachedAt": "…" } ] }
```
- "En curso desde 6:41, 24 min" se calcula con `startedAt`.
- "Compartiendo ubicación hace N s" = `position.ageSeconds`, o la hora del último lote aceptado localmente.
- "Siguiente parada, 4 de 7 · 850 m · 3 min" viene de `nextStop`; "A bordo 9 de 18", de `studentsOnBoard`/`totalStudents`.
- "Suben 4 alumnos" = pasajeros `Pending` con `stopId == nextStop.stopId`.
- "Registrar abordaje" muestra los pasajeros de la parada actual o siguiente, con nombre, grado y foto de Stakeholder.
  - `absenceSource: "Guardian"` → "Su apoderado avisó que hoy no asiste" (+ `absenceNote`).
  - "Faltan registrar N alumnos" = cantidad de `Pending`.
- Refrescar al volver a primer plano, después de cada acción y cada ~15–30 s.

#### d) GPS — `POST /api/v1/tracking/positions` · `RecordPositionsCommand`
```json
{ "tripId": "…",
  "readings": [ { "latitude": -12.1123, "longitude": -76.9856, "recordedAt": "2026-10-26T06:45:03-05:00",
                  "accuracyMeters": 8, "speedKmh": 24.5, "headingDegrees": 110 } ] }
```
Respuesta: `{ "accepted": 1, "ignored": 0, "rejected": 0, "lastAcceptedAt": "…" }`.
- Máximo **500 lecturas** por lote (si no, 400 `VALIDATION_BATCH_TOO_LARGE`). Límite de **120 peticiones por minuto por usuario**: ante un 429, esperar y sumar las lecturas al siguiente lote.
- En línea: una lectura cada 3–5 s y un envío cada 5–10 s. Sin señal: acumular y enviar en orden al reconectar.
- El servidor **ignora sin fallar** las lecturas ruidosas:
  - precisión mayor a 100 m;
  - velocidad mayor a 150 km/h;
  - más de 2 min en el futuro;
  - anteriores al inicio del viaje;
  - `(0,0)`.

  La app puede prefiltrarlas para ahorrar datos.
- **422 `TRACKING_REQUIRES_ACTIVE_TRIP` → detener el GPS de inmediato.** La ubicación se comparte **solo** con el viaje en curso.
- Efectos en el servidor: actualiza Redis y SignalR, y emite `vehicle-approaching-stop` (800 m) y `stop-reached` (60 m).

#### e) Pasajeros

| Botón | Ruta | Body | Comando | Evento |
|---|---|---|---|---|
| "Subió" | `POST /api/v1/trips/{tripId}/boardings` | `{ studentId, clientEventId, occurredAt?, position? }` | `RecordBoardingCommand` | `student-boarded` |
| "Bajó" / entregado | `POST /api/v1/trips/{tripId}/alightings` | `{ studentId, clientEventId, occurredAt?, position? }` | `RecordAlightingCommand` | `student-alighted` |
| "No vino" | `POST /api/v1/trips/{tripId}/absences` | `{ studentId, clientEventId, occurredAt? }` | `MarkStudentAbsentCommand` | `student-marked-absent` (`source: Driver`) |
| "Deshacer" | `POST /api/v1/trips/{tripId}/passengers/{studentId}/undo` | `{ clientEventId }` | `UndoPassengerRecordCommand` | `passenger-record-undone` |

- **Recojo (`Pickup`):** "Subió" o "No vino" por alumno. "Bajó" no hace falta: al finalizar, todos los que van a bordo quedan **entregados**.
- **Retorno (`Return`):** "Subió" al salir del colegio y "Bajó" en la parada de cada alumno. No se puede finalizar con alumnos a bordo.
- "Deshacer" revierte el último "Subió" o "No vino" de ese alumno.
- Errores (422): `STUDENT_NOT_IN_ROSTER`, `PASSENGER_INVALID_TRANSITION`, `TRIP_NOT_ACTIVE` y `EVENT_TIMESTAMP_INVALID`.
- **UI optimista:** actualizar la fila al instante ("Subió 6:58 · Deshacer"), encolar la acción y confirmar con la respuesta.

#### f) Incidente — `POST /api/v1/trips/{tripId}/incidents` · `ReportIncidentCommand`
```json
{ "clientEventId": "…", "incidentType": "HeavyTraffic", "detail": "Choque en Av. Benavides",
  "notifyGuardians": true, "occurredAt": "…", "position": { "latitude": -12.1, "longitude": -76.9 } }
```

| Botón del prototipo | `incidentType` | Severidad en Administration | ¿Pasa a `Delayed`? |
|---|---|---|---|
| Tráfico intenso | `HeavyTraffic` | LOW | Sí |
| Desvío de ruta | `RouteDeviation` | MEDIUM | Sí |
| Falla del vehículo | `VehicleFailure` | HIGH | Sí |
| Alumno se siente mal | `StudentUnwell` | HIGH | No |
| Accidente | `Accident` | CRITICAL | Sí |
| Otro | `Other` (exige `detail`) | MEDIUM | No |

- `detail` ≤ 500 caracteres. Errores: 422 `INCIDENT_DETAIL_REQUIRED` / `INCIDENT_DETAIL_TOO_LONG`, 400 `VALIDATION_INCIDENT_TYPE`.
- `notifyGuardians` corresponde al interruptor "Avisar a los padres de la ruta". La empresa **siempre** recibe el reporte.
- "Elige qué pasó para enviar": el botón Enviar se habilita solo con un tipo elegido.
- Sin señal: va a la cola como `type: "Incident"` y se envía con `/sync`.
- ⚠️ "Añadir foto" y "Nota de voz" no tienen soporte: ocultarlos o marcarlos como "Próximamente".

#### g) SOS — `POST /api/v1/trips/{tripId}/sos` · `RaiseSosCommand`
```json
{ "clientEventId": "…", "occurredAt": "…", "position": { "latitude": -12.1, "longitude": -76.9 } }
```
- La cuenta regresiva de **5 s es solo de la app**: si el conductor cancela, no se llama al backend. El `clientEventId` se genera al terminar la cuenta.
- **Nunca falla por falta de GPS**: `position` es opcional.
- Máxima prioridad: sin red, reintentar cada pocos segundos y además dejarlo en la cola. Ofrecer "Llamar a la empresa" como respaldo.
- Emite `sos-raised` → Administration crea una alerta CRITICAL abierta.
- "las familias de los N alumnos a bordo" = `studentsOnBoard`.
- Mientras `hasActiveSos = true`:
  - mostrar "SOS activo · tu empresa fue avisada";
  - **no** se puede finalizar (422 `TRIP_HAS_ACTIVE_SOS`): lo cierra el ADMIN con `/sos/resolve`.

#### h) Finalizar — `POST /api/v1/trips/{tripId}/complete` · `CompleteTripCommand`
```json
{ "clientEventId": "…" }
```
- **Antes de llamar:** vaciar la cola (`/sync`) y el GPS pendiente, y validar localmente que no queden pendientes, SOS activo ni (en retorno) alumnos a bordo.
- Errores:
  - 422 `TRIP_HAS_PENDING_STUDENTS` ("Faltan registrar N alumno(s)") → volver a la lista.
  - 422 `TRIP_HAS_STUDENTS_ON_BOARD`.
  - 422 `TRIP_HAS_ACTIVE_SOS`.
- Emite `trip-completed`; Administration guarda el `TripRecord`.
- Después: **detener la ubicación** ("Dejaste de compartir tu ubicación") y abrir el resumen.

#### i) Resumen — `GET /api/v1/trips/{tripId}/summary` · `GetTripSummaryQuery`
```json
{ "tripId": "…", "routeName": "Ruta 3", "direction": "Pickup", "status": "Completed",
  "startedAt": "…", "completedAt": "…", "delivered": 17,
  "absent": [ { "studentId": "…", "stopId": "…", "status": "Absent", "absenceSource": "Guardian", "absenceNote": "…", "boardedAt": null, "alightedAt": null } ],
  "incidents": [ { "incidentId": "…", "type": "HeavyTraffic", "detail": null, "occurredAt": "…" } ] }
```
- "de 6:41 a 7:53" sale de `startedAt`/`completedAt`.
- "17 alumnos llegaron al colegio" = `delivered`.
- "1 no vino: Camila Huamán (aviso del apoderado)" = `absent`, con el nombre de Stakeholder y el texto según `absenceSource`.
- "Todos los registros enviados" = la cola local quedó vacía.
- "Siguiente viaje" = el siguiente de la agenda.

#### j) Otras consultas
- `GET /api/v1/trips/{tripId}/position` → `{ tripId, status, position?, nextStopId, nextStopDistanceMeters, nextStopEtaMinutes }` (`GetTripPositionQuery`, liviana).
- `GET /api/v1/trips/{tripId}/track` → recorrido GPS aceptado (`GetTripTrackQuery`), para pintar la traza en el mapa.

### 7.4 Sincronización offline — `POST /api/v1/trips/{tripId}/sync` · `SyncOfflineEventsCommand`
```json
{ "events": [
  { "clientEventId": "…", "type": "Boarding",  "occurredAt": "…", "studentId": "…", "position": { "latitude": -12.1, "longitude": -76.9 } },
  { "clientEventId": "…", "type": "Absent",    "occurredAt": "…", "studentId": "…" },
  { "clientEventId": "…", "type": "Incident",  "occurredAt": "…", "incidentType": "HeavyTraffic", "detail": null, "notifyGuardians": false },
  { "clientEventId": "…", "type": "Sos",       "occurredAt": "…" }
] }
```
```json
{ "outcomes": [ { "clientEventId": "…", "status": "Applied",  "errorCode": null },
                { "clientEventId": "…", "status": "Rejected", "errorCode": "PASSENGER_INVALID_TRANSITION" } ],
  "applied": 3, "duplicates": 0, "rejected": 1 }
```
- `type`: `Boarding`, `Alighting`, `Absent`, `Undo`, `Incident`, `Sos`. Máximo **200** por llamada.
- El servidor los aplica **en orden de `occurredAt`**, una sola vez por `clientEventId`. Un evento inválido **no bloquea** a los demás.
- En la cola:
  - `Applied` y `Duplicate` → sacar de la cola.
  - `Rejected` → sacar de la cola y mostrar aviso; no reintentar indefinidamente.
  - Cola vacía → "Todos los registros enviados".
- `start` y `complete` **no** van por `/sync`: usan su propio endpoint, con el mismo `clientEventId` en cada reintento.
- Orden al reconectar: **1)** GPS pendiente en lotes, **2)** `/sync`, **3)** refrescar `/live`.

### 7.5 Tiempo real (opcional) — SignalR `/hubs/trip-tracking`
- Conexión: `{GW}/hubs/trip-tracking?access_token=<JWT>`, protocolo JSON, WebSocket (pasa por el Gateway).
- Llamar a `SubscribeToTrip(tripId)`, que devuelve `TripPositionDto`. Después llegan `PositionUpdated` y `TripStatusChanged { tripId, status, reason, occurredAt }`.
- Le sirve al conductor para enterarse, sin hacer polling, de que la empresa **canceló** el viaje (→ detener el GPS) o **resolvió el SOS**. Si no se usa, basta con refrescar `/live`.

---

## 8. Eventos de integración

La app **no** publica ni consume RabbitMQ. Esta tabla explica qué provoca cada botón y sirve para los textos de la UI ("Los padres verán…", "Tu empresa recibe…").

**Exchange `trip-tracking.events`** (topic, Outbox, entrega al menos una vez; `message_id` = id del evento; todos llevan `tripId`, `routeId` y `organizationId`; contratos `*V1`):

| Acción | Evento | Quién reacciona hoy |
|---|---|---|
| (el backend programa el viaje) | `trip-tracking.trip-scheduled` | — |
| Iniciar | `trip-tracking.trip-started` (+ `trip-delayed`) | (Notification, fuera de alcance) |
| GPS | `trip-tracking.vehicle-approaching-stop` (800 m), `trip-tracking.stop-reached` (60 m) | (Notification) |
| Subió / Bajó | `trip-tracking.student-boarded` / `student-alighted` | (Notification: aviso a padres) |
| No vino | `trip-tracking.student-marked-absent` | (Notification) |
| Deshacer | `trip-tracking.passenger-record-undone` | (Notification) |
| Incidente | `trip-tracking.incident-reported` (+ `trip-delayed`) | **Administration** → `OperationalAlert` por severidad |
| SOS | `trip-tracking.sos-raised` | **Administration** → alerta CRITICAL |
| (el ADMIN resuelve el SOS) | `trip-tracking.sos-resolved` | Administration |
| Finalizar | `trip-tracking.trip-completed` | **Administration** → `TripRecord` |
| (el ADMIN cancela) | `trip-tracking.trip-cancelled` | (Notification) |

Además, después de cada commit, el canal en vivo `trip-tracking.live` (`live-position`, `live-status`) alimenta el hub de SignalR mediante Redis.

**Eventos que alimentan lo que ve el conductor** (cola `trip-tracking.inbound`):

| Evento | Lo publica | Efecto visible en la app |
|---|---|---|
| `administration.route-published` | Administration al activar una ruta | Aparecen los viajes de hoy y mañana en `/drivers/me/trips` |
| `administration.route-updated` | Administration al editar una ruta activa (paradas, alumnos inscritos, conductor, vehículo) | Los viajes `Scheduled` se actualizan (alumnos nuevos o retirados, paradas). ⚠️ Un cambio de **hora** no mueve un viaje ya programado |
| `administration.route-retired` | Administration al desactivar | Dejan de programarse viajes de esa ruta |
| `stakeholder.guardian-student-linked` / `unlinked` | Stakeholder | Solo afecta a la app del padre |
| `stakeholder.student-enrolled` / `student-enrollment-ended` | Stakeholder | Indirecto: la ruta cambia con `route-updated`. Refrescar `/stakeholders/drivers/me/students` antes de cada viaje |
| `administration.route-assigned` | Administration | Trip & Tracking no lo usa |

---

## 9. Errores: catálogo y reacción de la app

Formato común (servicios y Gateway):
```json
{ "type": "https://httpstatuses.io/422", "title": "Regla de negocio incumplida", "status": 422,
  "detail": "Faltan registrar 2 alumno(s).", "code": "TRIP_HAS_PENDING_STUDENTS", "traceId": "00-…" }
```

| HTTP | `code` | Acción de la app |
|---|---|---|
| 400 | `VALIDATION_*` (`VALIDATION_CLIENT_EVENT_ID`, `VALIDATION_INCIDENT_TYPE`, `VALIDATION_BATCH_TOO_LARGE`, …) | Bug del cliente: registrar y no reintentar igual |
| 401 | `UNAUTHORIZED`, `INVALID_TOKEN`, `TOKEN_EXPIRED`, `InvalidCredentials` | Ir al login; conservar la cola |
| 403 | `FORBIDDEN`, `IAM_ACCOUNT_REJECTED`, `OrganizationNotActive` | Cerrar sesión con mensaje |
| 403 | `TRIP_ACCESS_DENIED`, `TRIP_DRIVER_MISMATCH`, `ORGANIZATION_REQUIRED` | No reintentar; refrescar la agenda |
| 404 | `TRIP_NOT_FOUND`, `ROUTE_NOT_FOUND`, `DriverNotFound`, `GATEWAY_ROUTE_NOT_FOUND` | Refrescar o mostrar mensaje |
| 409 | `CONCURRENCY_CONFLICT`, `DUPLICATE_RECORD` | **Reintentar con el mismo `clientEventId`** |
| 422 | `TRIP_START_TOO_EARLY`, `TRIP_INVALID_TRANSITION`, `TRIP_NOT_ACTIVE`, `TRIP_HAS_PENDING_STUDENTS`, `TRIP_HAS_STUDENTS_ON_BOARD`, `TRIP_HAS_ACTIVE_SOS`, `STUDENT_NOT_IN_ROSTER`, `PASSENGER_INVALID_TRANSITION`, `EVENT_TIMESTAMP_INVALID`, `INCIDENT_DETAIL_REQUIRED`, `INCIDENT_DETAIL_TOO_LONG`, `ROUTE_NOT_OPERATING` | Mostrar `detail`; no reintentar igual |
| 422 | `TRACKING_REQUIRES_ACTIVE_TRIP` | **Detener el GPS** |
| 429 | `TOO_MANY_REQUESTS` (Gateway) o sin `code` (rate limit de telemetría) | Esperar; agrupar lecturas |
| 502 / 504 | `UPSTREAM_UNAVAILABLE` / `UPSTREAM_TIMEOUT` | Encolar y reintentar con backoff |
| 503 | `IAM_UNAVAILABLE` | Reintentar con backoff (respetar `Retry-After`) |
| sin red / 5xx | — | Encolar y reintentar con backoff exponencial y el mismo `clientEventId` |

---

## 10. Reglas de dominio que la UI debe respetar

| Regla | Valor | Uso en la UI |
|---|---|---|
| Ventana de inicio | Hasta **60 min antes** de `scheduledStart` | "Iniciar viaje" deshabilitado antes; mostrar "Disponible desde HH:MM" |
| Inicio tardío | Más de **10 min** después → `Delayed` | Etiqueta "Con retraso" |
| Precisión GPS | ≤ **100 m** aceptada | Checklist: verde ≤ 30 m, ámbar ≤ 100 m, rojo si es mayor |
| Velocidad / reloj | ≤ 150 km/h; ≤ 2 min hacia el futuro | Prefiltrar; usar la hora del sistema |
| Cercanía / llegada | 800 m / 60 m | Lo calcula el backend; la app muestra `nextStop` |
| Finalizar | Sin `Pending`, sin SOS activo y, en retorno, sin alumnos a bordo | Validar localmente antes de llamar |
| Lotes | GPS ≤ 500 · sync ≤ 200 | Partir los lotes |
| Detalle del incidente | ≤ 500 caracteres | Contador en el campo |
| Zona horaria | **America/Lima** | Agenda y horas mostradas |
| Privacidad | Ubicación solo con el viaje activo; foto del menor solo con token | Aviso en Inicio; no cachear fotos fuera del almacenamiento de la app |

---

## 11. Brechas conocidas (decidir con el equipo)

| # | Brecha | Impacto en la app | Propuesta |
|---|---|---|---|
| 1 | Login solo por **correo** (el prototipo dice "DNI o correo") | Cambiar la etiqueta | Opcional: sign-in por documento en IAM |
| 2 | **Sin refresh token** ni revocación (TS-01); token de 8 h | Re-login si vence (§4.3) | Implementar TS-01 |
| 3 | **SOAT** no existe en el modelo de Vehicle | Ocultar la fila o mostrar "—" | Agregar SOAT al Roster |
| 4 | Incidente sin **foto ni nota de voz** | Ocultar o marcar "Próximamente" | Endpoint de adjuntos + S3 |
| 5 | Sin recuperación de contraseña | Solo el texto informativo del prototipo | — |
| 6 | Notification incompleto | La app no registra token push ni tiene bandeja | Fuera de alcance |
| 7 | Alumnos agregados a mano por el ADMIN sin ficha en Stakeholder | Mostrar "Alumno sin ficha" | Cerrar `PUT /routes/{id}/stops/{stopId}/students/{studentId}` |
| 8 | Seed de demo incompleto (el conductor de `IamSeeder` no está en el Roster ni tiene rutas) | Usar la prueba de humo o los pasos de la §12 para tener datos | Seed coherente |
| 9 | Un cambio de hora de salida no mueve un viaje ya programado | Puede verse la hora anterior hasta el día siguiente | Ajustar Trip & Tracking |
| 10 | HTTP sin TLS en local | Celular físico: permitir *cleartext* solo en dev, o usar ngrok | HTTPS en el despliegue |

---

## 12. Cómo levantar el backend y preparar un conductor de prueba

```bash
git clone https://github.com/upc-pre-202610-1asi0657-9206-nimbuspath/rutasegura-backend.git
cd rutasegura-backend && git checkout develop
docker compose up -d --build                      # Gateway :8000, Monolito :5000, Trip-Tracking :5180, MySQL, RabbitMQ, Redis
docker compose --profile smoke run --rm smoke     # e2e: crea empresa, conductor, ruta, familia y viaje (75 OK)
```

Datos para un conductor (lo mismo que hace `scripts/smoke/e2e.sh`). **El paso 1 va directo al monolito** (`:5000`), porque el Gateway no expone el provisioning; el resto, por el Gateway (`:8000`):

1. **Empresa + ADMIN:** `POST http://localhost:5000/api/v1/organizations`, cabecera `X-Provisioning-Key: local-docker-provisioning-key-0123456789abcdef`:
   `{ "name": "Transportes Santa Rosa", "administrator": { "firstName": "Ana", "lastName": "Rosa", "email": "admin@santarosa.pe", "password": "Password123" } }`
2. **Token del ADMIN:** `POST {GW}/api/v1/users/sign-in`.
3. **Cuenta del conductor:** `POST {GW}/api/v1/users/drivers` `{ firstName, lastName, email, password }` → `id` (IAM).
4. **Roster:** `POST {GW}/api/v1/roster/drivers` `{ userId, firstName, lastName, documentNumber, licenseNumber, licenseCategory, licenseExpiresOn, phoneNumber }` → `id`; después `POST /roster/drivers/{id}/verify`.
5. **Vehículo:** `POST {GW}/api/v1/roster/vehicles` `{ licensePlate, seatingCapacity, brand, model }`.
6. **Ruta:**
   - `POST {GW}/api/v1/routes` `{ name, direction: "PICKUP", departureTime: "HH:MM:00", estimatedArrivalTime, timeZoneId: "America/Lima", operatingDays: [...] }`.
   - Paradas: `POST /routes/{id}/stops` (≥ 60 m entre sí).
   - Asignación: `PUT /routes/{id}/assignment` `{ driverId: <id Roster>, vehicleId }`.
   - Activar: `POST /routes/{id}/activate`.
   - Elegir `departureTime` dentro de los próximos 60 min para poder iniciar el viaje enseguida.
7. **Alumnos con nombre (recomendado):**
   - Un padre se registra con `POST {GW}/api/v1/users` (rol PARENT) y su perfil con `PUT /stakeholders/me/profile`.
   - Registra a su hijo: `POST /stakeholders/me/students` (con `pickupLocation`).
   - Opcional, foto: `PUT /stakeholders/me/students/{id}/photo`.
   - Inscribe al hijo con el código de la ruta (`POST /routes/{id}/enrollment-code` como ADMIN): `POST /stakeholders/me/students/{id}/enrollments` `{ "enrollmentCode": "…" }`.
8. **Ver el viaje:** esperar unos 10 s (programador) y llamar a `GET {GW}/api/v1/drivers/me/trips` con el token del conductor.

Alternativa sin monolito, solo para desarrollo: perfil `http-demo-sin-iam` de Trip & Tracking (DevTokens en `/dev/tokens` y datos demo "Ruta 3", **directo a `:5180`**, porque el Gateway bloquea `/dev`).

---

## 13. Recomendaciones de implementación

**Stack:** a definir por el equipo (Flutter, React Native/Expo o Kotlin nativo). Cualquier opción debe cubrir:
- **Una URL base configurable** por *flavor* o variable de entorno (`API_BASE_URL` = Gateway).
- **Cliente HTTP:**
  - interceptor de `Authorization` y `X-Correlation-Id`;
  - parseo de ProblemDetails a `ApiError { status, code, detail, correlationId }`;
  - timeouts y reintentos con backoff solo para operaciones idempotentes (todas las del conductor lo son por `clientEventId`).
- **Ubicación en segundo plano** con servicio en primer plano (Android: notificación "Compartiendo ubicación"; iOS: background location). Activa **solo** entre `start` y `complete`, o hasta recibir `TRACKING_REQUIRES_ACTIVE_TRIP`.
- **Persistencia local** (SQLite: Room, Drift, WatermelonDB, expo-sqlite) para:
  - la cola de acciones (`clientEventId`, `tripId`, tipo, payload, `occurredAt`, intentos, estado);
  - el buffer de GPS;
  - la caché de agenda, `/live`, perfil, vehículo y **alumnos de Stakeholder** (más las fotos), para abrir sin señal.
- **Sincronizador** disparado por conectividad: GPS → `/sync` → `/live`, con backoff exponencial.
- **Seguridad:**
  - token en almacenamiento seguro;
  - no registrar token, posiciones ni fotos en los logs;
  - fotos de menores solo en el almacenamiento privado de la app;
  - HTTPS en producción.
- **Usabilidad al conducir:** botones grandes, pocas acciones por pantalla y confirmación solo en SOS y Finalizar.

**Flujo de navegación**
```
Login ─► Inicio (agenda + checklist) ─► [Iniciar viaje] ─► Viaje en curso ◄──► Registrar abordaje
   ▲            │                                            │  ├──► Reportar incidente
   │            └──► Perfil ─► Cerrar sesión ─┐              │  └──► SOS (5 s) ─► SOS activo
   └─────────────────────────────────────────┘              └──► [Finalizar] ─► Viaje finalizado ─► Inicio
```

---

## 14. Checklist de aceptación (Sprint)

- [ ] Toda la app usa **solo la URL del Gateway**.
- [ ] Login rechaza cuentas que no son `DRIVER` y maneja 401, 403 y 429.
- [ ] Inicio muestra la agenda de hoy con placa, paradas y alumnos, y retoma un viaje en curso.
- [ ] Perfil muestra licencia, empresa, vehículo y rutas; maneja 404 `DriverNotFound`.
- [ ] Los alumnos de Stakeholder (nombre, grado, foto con token) quedan en caché y se ven sin señal.
- [ ] "Iniciar viaje" respeta la ventana de 60 min y arranca el GPS.
- [ ] El GPS va en lotes, solo con el viaje activo, y se detiene con `TRACKING_REQUIRES_ACTIVE_TRIP`, al finalizar o si el viaje se cancela.
- [ ] Subió / No vino / Bajó / Deshacer con `clientEventId`, UI optimista y cola offline.
- [ ] Incidente con los 6 tipos y "Avisar a los padres"; sin señal se envía al reconectar.
- [ ] SOS con 5 s de cancelación, sin depender del GPS y con reintento prioritario.
- [ ] Finalizar valida pendientes, SOS y alumnos a bordo, y muestra el resumen con nombres.
- [ ] Modo avión → acciones encoladas → al reconectar `/sync` → "Todos los registros enviados".
- [ ] Ningún reintento genera duplicados (mismo `clientEventId`).

---

### Referencias en el repositorio
- `src/ApiGateway/README.md` y `appsettings.json`: rutas y errores del Gateway.
- `src/Services/Trip-Tracking/docs/TRIP-TRACKING-CONTRATOS.md`, `README.md` y `RutaSegura.TripTracking.Api.http`: contrato y recorrido completo.
- `src/Monolith/Iam/README.md`, `src/Monolith/Administration/README.md` y `src/Monolith/Stakeholder/README.md`.
- `scripts/smoke/e2e.sh`: secuencia real de punta a punta (pasos 5b y 6b por el Gateway).
