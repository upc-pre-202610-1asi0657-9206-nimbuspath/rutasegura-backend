# Stakeholder — RutaSegura Monolith

Bounded context de las **familias**: el padre (rol `PARENT`) registra a sus hijos (nombre, datos del colegio, foto y dirección de recojo) y los inscribe en las rutas que crea la empresa de transporte. La inscripción **actualiza la planificación de la ruta**: Route Planning (Administration) coloca al niño en la parada de su dirección y, si no hay una cerca, crea una.

Capas: `Domain` (agregados `Student` y `GuardianProfile`, entidad `RouteEnrollment`), `Application` (casos de uso y puertos), `Infrastructure` (EF Core, ACLs hacia Route Planning e Iam, outbox y RabbitMQ) e `Interfaces` (REST). Todo se expone **solo por el API Gateway** con el prefijo `/api/v1/stakeholders/**`.

## Cómo se planifica la ruta con las direcciones de recojo

1. La empresa crea la ruta (DRAFT o ACTIVE) y comparte su **código de inscripción** (`enrollmentCode`, 8 caracteres sin 0/O ni 1/I/L, p. ej. `K7QM4P2X`). El ADMIN lo ve en `GET /api/v1/routes/{id}` y puede regenerarlo con `POST /api/v1/routes/{id}/enrollment-code`. El conductor no lo ve.
2. El padre ve la ruta del código (`GET /route-offers/{code}`: empresa, sentido, horario, asientos libres) e inscribe a su hijo (`POST /me/students/{id}/enrollments`).
3. Stakeholder llama a la fachada publicada `IRoutePlanningContextFacade` de Administration, **en la misma transacción**:
   - Si hay una parada a **≤ 150 m** de la dirección de recojo, el niño va a esa parada.
   - Si no, se crea una parada `ENROLLMENT` en esa dirección, insertada donde alarga menos el recorrido (*cheapest insertion*). El ADMIN puede reordenar después.
   - Se respeta el aforo del vehículo (`RouteFull`) y el máximo de 30 paradas (`RouteStopLimitReached`). Una ruta INACTIVE no acepta inscripciones.
   - Si la ruta está ACTIVE se publica `administration.route-updated` y Trip & Tracking **actualiza los viajes ya programados** que aún no empiezan.
4. Si el padre cambia la dirección de recojo, el niño se mueve de parada en todas sus rutas. Si se retira o lo borra, sale de la ruta. Una parada `ENROLLMENT` que se queda sin alumnos se elimina; las paradas manuales del ADMIN se conservan. Una parada automática que el ADMIN edita pasa a ser manual.

Si Route Planning rechaza el cambio, no se guarda nada (transacción con `IUnitOfWork.BeginTransactionAsync`).

## Endpoints (`/api/v1/stakeholders`)

| Política | Método y ruta | Uso |
| :--- | :--- | :--- |
| PARENT | `GET/PUT /me/profile` | Teléfono y documento del padre (nombre y correo vienen de Iam) |
| PARENT | `GET/POST /me/students`, `GET/PUT/DELETE /me/students/{id}` | Hijos: nombre, fecha de nacimiento, grado, colegio, dirección de recojo |
| PARENT | `PUT /me/students/{id}/pickup-location` | Nueva dirección de recojo (mueve al niño de parada en sus rutas) |
| PARENT | `PUT /me/students/{id}/photo` (multipart, campo `photo`), `DELETE …/photo` | Foto JPEG/PNG/WebP de hasta 1 MB (se valida el contenido, no el nombre) |
| PARENT | `GET /route-offers/{enrollmentCode}` | Ruta detrás de un código, antes de inscribir |
| PARENT | `POST /me/students/{id}/enrollments` `{ enrollmentCode }`, `DELETE …/enrollments/{enrollmentId}` | Inscribir y retirar |
| ADMIN | `GET /enrollments?routeId=&onlyActive=true`, `POST /enrollments/{id}/cancel` `{ reason }` | Inscripciones de su empresa; la empresa puede terminar una |
| ADMIN o DRIVER | `GET /routes/{routeId}/students` | Niños de una ruta: nombre, grado, foto, parada, teléfono del padre. El DRIVER solo ve sus rutas (`RouteNotOperatedByDriver`) y no ve el correo del padre |
| DRIVER | `GET /drivers/me/students` | Niños de todas sus rutas (para la caché offline de la app) |
| Cualquier cuenta | `GET /students/{id}/photo` | Foto. Solo el padre, el ADMIN de una empresa donde el niño está inscrito y el conductor de esa ruta (`StudentAccessDenied`) |

Errores en ProblemDetails con `code`: 404 `StudentNotFound`, `EnrollmentCodeNotFound`, `EnrollmentNotFound`, `RouteNotFound`, `StudentPhotoNotFound`; 403 `RouteNotOperatedByDriver`, `StudentAccessDenied`; 400 `InvalidStakeholderData`; 422 `StudentPhotoInvalid`; 409 `StudentLimitReached`, `StudentAlreadyEnrolled`, `EnrollmentDirectionTaken`, `RouteNotAcceptingEnrollments`, `RouteFull`, `RouteStopLimitReached`, `OrganizationNotActive`, `StudentHasActiveEnrollments`; 503 `RoutePlanningUnavailable`.

## Reglas de negocio

| Regla | Dónde | Error |
| :--- | :--- | :--- |
| Solo el padre que registró al niño lo gestiona (los de otros padres responden 404) | `Student`, `StakeholderCommandService` | `StudentNotFound` |
| Máximo 10 hijos por padre; edad escolar (2 a 20 años) | `Student` | `StudentLimitReached`, `InvalidStakeholderData` |
| Una inscripción activa por ruta y una por sentido (recojo / retorno) | `Student.Enroll` | `StudentAlreadyEnrolled`, `EnrollmentDirectionTaken` |
| No se inscribe en rutas INACTIVE ni de empresas suspendidas | `StakeholderCommandService` | `RouteNotAcceptingEnrollments`, `OrganizationNotActive` |
| Para borrar al niño se terminan antes sus inscripciones (el servicio lo hace y libera los asientos) | `Student.Remove` | `StudentHasActiveEnrollments` |
| La foto de un menor nunca es pública (AC-01) | `StakeholderQueryService` | `StudentAccessDenied` |

## Persistencia (migración `AddStakeholderBoundedContext`)

| Tabla | Contenido |
| :--- | :--- |
| `stakeholder_students` | Agregado Student: nombre, datos escolares, recojo y metadatos de la foto como *complex types* |
| `stakeholder_route_enrollments` | Inscripciones (FK `student_id`, cascada) |
| `stakeholder_guardian_profiles` | Teléfono y documento del padre (PK = id de Iam) |
| `stakeholder_student_photos` | Bytes de la foto (`mediumblob`), aparte del agregado; se puede reemplazar por S3 |
| `stakeholder_outbox_messages` | Outbox de `stakeholder.events` |

La misma migración agrega a Administration `route_itineraries.enrollment_code` (único; las rutas existentes reciben un código al migrar) y `route_stops.origin` (`MANUAL` por defecto).

## Integración

- **Route Planning:** solo por `Monolith.Administration.Interfaces.ACL.IRoutePlanningContextFacade` (adaptador `Infrastructure/Acl/RoutePlanningService`). Administration no depende de Stakeholder.
- **Iam:** solo por `IIamContextFacade` (nombres, correo, nombre de la empresa, empresa activa) y la política nueva `IamPolicies.Guardian`.
- **RabbitMQ** (exchange `stakeholder.events`, outbox con *publisher confirms*):

| Tipo de mensaje | Contrato | Consumidor |
| :--- | :--- | :--- |
| `stakeholder.guardian-student-linked` / `-unlinked` | `GuardianStudentLinkV1 { guardianId, studentId }` | Trip & Tracking (`GuardianAccessPolicy`: el padre sigue los viajes de su hijo) |
| `stakeholder.student-enrolled` / `-enrollment-ended` | `StudentEnrollmentV1` | Nadie aún (pensado para Notification) |

La conexión y `Enabled` se toman de `Administration:Messaging`; `Stakeholder:Messaging` puede sobrescribirlos.

## Pruebas

```powershell
dotnet test tests/RutaSegura.Monolith.Stakeholder.Tests
dotnet test tests/RutaSegura.Monolith.Administration.IntegrationTests --filter "FullyQualifiedName~Stakeholder"
```

- `RutaSegura.Monolith.Stakeholder.Tests` (55): dominio, casos de uso con dobles (transacción y rollback incluidos), permisos de la foto y reglas de capas leídas del IL (Stakeholder usa Administration solo por su fachada; Administration no conoce Stakeholder).
- `StakeholderEndToEndTests` (14, host real con SQLite): flujo completo padre → código → parada automática, paradas compartidas y orden, cambio de dirección, aforo con rollback, reglas de inscripción, vista del conductor y del ADMIN, foto, cancelación por la empresa, borrado y seguridad.
- `scripts/smoke/e2e.sh`, paso 6b: lo mismo por el Gateway con MySQL, RabbitMQ y Trip & Tracking reales.
