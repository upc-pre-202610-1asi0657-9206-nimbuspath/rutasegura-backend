# IAM (Identity and Access Management) — RutaSegura Monolith

El módulo de **IAM** es el Bounded Context responsable de la gestión de identidades, autenticación basada en JSON Web Tokens (JWT), control de acceso basado en roles (RBAC) y la administración de organizaciones (*tenants*) dentro de la arquitectura de RutaSegura.

---

## 🏗️ CQRS: Comandos y Eventos de Dominio

A continuación se especifican los Comandos ejecutados y los Eventos generados por cada acción dentro del Bounded Context.

| Ruta HTTP | Tipo | Comando (CQRS) | Evento de Dominio / Integración |
| :--- | :--- | :--- | :--- |
| `POST /api/v1/organizations` | Command | `CreateOrganizationCommand` | `OrganizationCreatedEvent` |
| `GET /api/v1/organizations/{id}` | Query | `GetOrganizationByIdQuery` | *N/A (Sólo Lectura)* |
| `PUT /api/v1/organizations/{id}` | Command | `UpdateOrganizationCommand` | `OrganizationUpdatedEvent` |
| `POST /api/v1/users` | Command | `SignUpCommand` | `UserRegisteredEvent` |
| `POST /api/v1/users/sign-in` | Command | `SignInCommand` | `UserAuthenticatedEvent` |
| `GET /api/v1/users` | Query | `GetAllUsersQuery` | *N/A (Sólo Lectura)* |
| `GET /api/v1/users/{id}` | Query | `GetUserByIdQuery` | *N/A (Sólo Lectura)* |

---

## 📌 Endpoints Expuestos y Especificación JSON

### 🏢 1. Organizations (Organization / Tenant Management)

#### **POST** `/api/v1/organizations`
Crea una nueva organización (escuela o entidad educativa) en la plataforma.

* **Comando:** `CreateOrganizationCommand`
* **Evento Publicado:** `OrganizationCreatedEvent`

* **Request Body:**
```json
{
  "name": "Colegio San Ignacio"
}
```

* **Response (201 Created):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Colegio San Ignacio",
  "createdAt": "2026-10-23T14:00:00Z"
}
```

---

#### **GET** `/api/v1/organizations/{organizationId}`
Obtiene los datos detallados de una organización específica.

* **Query:** `GetOrganizationByIdQuery`

* **Response (200 OK):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Colegio San Ignacio",
  "createdAt": "2026-10-23T14:00:00Z",
  "updatedAt": "2026-10-23T14:00:00Z"
}
```

* **Response (404 Not Found):**
```json
{
  "message": "Organization with ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 was not found."
}
```

---

#### **PUT** `/api/v1/organizations/{organizationId}`
Actualiza la información general de una organización existente.

* **Comando:** `UpdateOrganizationCommand`
* **Evento Publicado:** `OrganizationUpdatedEvent`

* **Request Body:**
```json
{
  "name": "Colegio San Ignacio de Loyola"
}
```

* **Response (200 OK):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Colegio San Ignacio de Loyola",
  "updatedAt": "2026-10-23T15:30:00Z"
}
```

---

### 👤 2. Users & Authentication

#### **POST** `/api/v1/users` (Sign Up)
Registra una nueva cuenta de usuario en el sistema vinculada a una organización.

* **Comando:** `SignUpCommand`
* **Evento Publicado:** `UserRegisteredEvent`

* **Request Body:**
```json
{
  "firstName": "Sebastian",
  "lastName": "Sanchez",
  "email": "sebastian03@gmail.com",
  "password": "Password123*",
  "roleTier": "Parent",
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

* **Response (201 Created):**
```json
{
  "id": "e8d21a94-1188-4c31-92bb-187e148e67a2",
  "firstName": "Sebastian",
  "lastName": "Sanchez",
  "email": "sebastian03@gmail.com",
  "roleTier": "Parent",
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "createdAt": "2026-10-23T14:05:00Z"
}
```

---

#### **POST** `/api/v1/users/sign-in` (Sign In)
Autentica las credenciales de usuario y retorna el token JWT necesario para consumir endpoints protegidos.

* **Comando:** `SignInCommand`
* **Evento Publicado:** `UserAuthenticatedEvent`

* **Request Body:**
```json
{
  "email": "sebastian03@gmail.com",
  "password": "Password123*"
}
```

* **Response (200 OK):**
```json
{
  "id": "e8d21a94-1188-4c31-92bb-187e148e67a2",
  "email": "sebastian03@gmail.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJlOGQyMWE5NC0xMTg4LTRjMzEtOTJiYi0xODdlMTQ4ZTY3YTIiLCJuYW1lIjoiRGllZ28gTWlyYW5kYSIsImlhdCI6MTUxNjIzOTAyMn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c",
  "tokenType": "Bearer",
  "expiresIn": 86400
}
```

* **Response (401 Unauthorized):**
```json
{
  "message": "Invalid email or password credentials."
}
```

---

#### **GET** `/api/v1/users`
Obtiene la lista de todos los usuarios registrados en el sistema.

* **Query:** `GetAllUsersQuery`

* **Response (200 OK):**
```json
[
  {
    "id": "e8d21a94-1188-4c31-92bb-187e148e67a2",
    "firstName": "Sebastian",
    "lastName": "Sanchez",
    "email": "sebastian03@gmail.com",
    "roleTier": "Parent",
    "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  },
  {
    "id": "7b309f3e-8c3b-419f-b7e1-884cfb7d6023",
    "firstName": "Maria",
    "lastName": "Perez",
    "email": "maria.perez@rutasegura.com",
    "roleTier": "Admin",
    "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  }
]
```

---

#### **GET** `/api/v1/users/{userId}`
Obtiene el perfil detallado de un usuario por su identificador único.

* **Query:** `GetUserByIdQuery`

* **Response (200 OK):**
```json
{
  "id": "e8d21a94-1188-4c31-92bb-187e148e67a2",
  "firstName": "Sebastian",
  "lastName": "Sanchez",
  "email": "sebastian03@gmail.com",
  "roleTier": "Parent",
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "createdAt": "2026-10-23T14:05:00Z"
}
```

---

## 🔒 Seguridad y Autenticación

Todas las peticiones a endpoints protegidos (a excepción de `POST /users` y `POST /users/sign-in`) deben incluir la cabecera HTTP estándar con el JWT recibido:

```http
Authorization: Bearer <tu_token_jwt>
```

---

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