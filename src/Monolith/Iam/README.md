# IAM (Identity and Access Management) — RutaSegura Monolith

El módulo de **IAM** es el Bounded Context responsable de la gestión de identidades, autenticación basada en JSON Web Tokens (JWT), control de acceso basado en roles (RBAC) y la administración de organizaciones (*tenants*) dentro de la arquitectura de RutaSegura.

---

## 👥 Quién crea cada cuenta

| Rol | Cómo se crea | Endpoint |
| :--- | :--- | :--- |
| **ADMIN** | Uno solo por organización, creado junto con la organización cuando se confirma el pago de la suscripción (Subscription llamará a `IIamContextFacade.ProvisionOrganizationAsync`). Mientras Subscription no exista, lo hace la plataforma con la cabecera `X-Provisioning-Key`. | `POST /api/v1/organizations` |
| **DRIVER** | Lo crea el ADMIN de la organización del token (después se registra en el Roster de Administration). | `POST /api/v1/users/drivers` |
| **PARENT** | Se registra solo desde la app. `roleTier` distinto de PARENT → 403 `SelfRegistrationNotAllowed`. | `POST /api/v1/users` |

## 📌 Endpoints

| Ruta | Acceso | Comando / Query |
| :--- | :--- | :--- |
| `POST /api/v1/organizations` | Plataforma (`X-Provisioning-Key`) | `ProvisionOrganizationCommand` (organización + su ADMIN) |
| `GET /api/v1/organizations/{id}` | Miembros de esa organización | `GetOrganizationByIdQuery` |
| `PUT /api/v1/organizations/{id}` | ADMIN de esa organización | `UpdateOrganizationCommand` |
| `POST /api/v1/users` | Anónimo (solo PARENT) | `SignUpCommand` |
| `POST /api/v1/users/sign-in` | Anónimo | `SignInCommand` |
| `POST /api/v1/users/drivers` | ADMIN | `RegisterDriverAccountCommand` |
| `GET /api/v1/users/me` | Cualquier cuenta | `GetUserByIdQuery` |
| `GET /api/v1/users/{id}` | La propia cuenta o el ADMIN de su organización | `GetUserByIdQuery` |
| `GET /api/v1/users` | ADMIN (solo cuentas de su organización) | `GetUsersByOrganizationQuery` |

Otras organizaciones o cuentas ajenas responden 404 (no se revela que existen). Errores en RFC 7807 con `code`.

**Provisioning** (`POST /api/v1/organizations`, cabecera `X-Provisioning-Key: <Iam:Provisioning:ApiKey>`):
```json
{ "name": "Movilidad Escolar Sur",
  "administrator": { "firstName": "Ana", "lastName": "Sur", "email": "admin@movilidadsur.pe", "password": "Password123" } }
```
Responde 201 con `{ organization, administrator }`. Sin clave configurada el endpoint responde 404; sin cabecera 401; clave incorrecta 403.

**Reglas**
- **Contraseñas:** de 8 a 72 caracteres, con al menos una letra y un dígito (`WeakPassword` → 400).
- **Sign-in:** una credencial incorrecta responde 401 `InvalidCredentials`. Una cuenta de organización suspendida responde 403 `OrganizationNotActive`.
- **Token:** dura `TokenSettings:ExpirationHours` (8 h) y lleva `iss` = `rutasegura-iam` y `aud` = `rutasegura-api`, que se validan.

## 🛡️ Seguridad que Iam provee a los demás bounded contexts

- `AddIamSecurity(configuration)` (Infrastructure/Authorization) centraliza la validación del JWT (secreto, issuer y audience) y responde 401/403 en formato ProblemDetails.
- Políticas en `Monolith.Iam.Interfaces.Security.IamPolicies`: `Account`, `OrganizationAdministrator` (ADMIN + `organizationId`) y `OrganizationDriver` (DRIVER + `organizationId`).
- Cada política verifica además que la cuenta siga válida en Iam (`ActiveIamAccountHandler`): existe, tiene el mismo rol y organización que el token, y la organización está ACTIVE.
- `ClaimsPrincipalExtensions` e `IamClaimTypes` leen el token. La fachada `IIamContextFacade` e `IamRoles` es lo único que otros contextos usan de Iam.

## 🔑 Secretos y configuración

`appsettings.json` (versionado) **no** contiene secretos.
- **En desarrollo:** `appsettings.Development.json` trae la cadena de conexión local, `TokenSettings:Secret` y una clave de provisioning de desarrollo.
- **En cualquier entorno desplegado:** hay que definirlos como variables de entorno o en un almacén de secretos:
  - `ConnectionStrings__DefaultConnection`
  - `TokenSettings__Secret` (mínimo 32 caracteres; el mismo valor en `Jwt__Secret` de Trip-Tracking)
  - `Iam__Provisioning__ApiKey` (opcional; vacío deshabilita el provisioning)

El secreto anterior estuvo versionado en Git: hay que generar uno nuevo para cualquier entorno real.

## 🚀 Pasos para Ejecutar la Solución

### Prerrequisitos
* **.NET SDK 10** 
* **Docker & Docker Compose** (para ejecutar la base de datos y/o servicios dependientes)
* **Postman / Insomnia** o **Swagger UI** para probar las peticiones.

---

### 1. Clonar el Repositorio y Ubicarse en el Proyecto
```bash
git clone https://github.com/tu-organizacion/rutasegura-monolith.git
cd rutasegura-monolith
```

---

---

### 3. Iniciar Servicios Dependientes (Base de Datos)
Utilización de Docker Compose para la base de datos (SQL Server):

```bash
docker-compose up -d
```

---

### 4. Aplicar Migraciones de Base de Datos
Ejecuta las migraciones necesarias para crear las tablas del Bounded Context de IAM (`Users`, `Organizations`, `Roles`, etc.):

```bash
dotnet ef database update
```

---

### 5. Compilar y Ejecutar la Aplicación

#### Opción A: Desde la línea de comandos (.NET CLI)
```bash
dotnet build
dotnet run --project src/Monolith.csproj
```

#### Opción B: Utilizando Docker
```bash
docker build -t rutasegura-api .
docker run -p 8080:8080 rutasegura-api
```

---

### 6. Verificación y Pruebas
Una vez iniciada la aplicación:

* **Swagger UI:** Navega a `http://localhost:5000/swagger/index.html` para visualizar y probar interactivamente los endpoints de IAM.
* **Health Check:** Verifica el estado del servicio en `http://localhost:5000/health`.