# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite + Filesystem storage
**Status**: Active development — core API, auth, and UI implemented

---

## Quick Start

```bash
git clone https://github.com/youruser/ObjeX.git
cd ObjeX/src/ObjeX.Api
dotnet run
```

Open **http://localhost:8080** — log in with `admin` / `admin`.

- **Blazor UI**: http://localhost:8080
- **API docs (Scalar)**: http://localhost:8080/scalar/v1
- **Job dashboard**: http://localhost:8080/hangfire
- **Health check**: http://localhost:8080/health

> ⚠️ Change the default admin credentials before exposing the instance publicly. Set `DefaultAdmin:Username`, `DefaultAdmin:Email`, and `DefaultAdmin:Password` in `appsettings.json` or environment variables.

---

## API Authentication

All API endpoints require authentication via **cookie session** (browser) or **API key** (external clients).

### API Key

Create a key in the Settings page (`/settings`) or via the API:

```bash
# 1. Log in to get a session cookie
curl -c cookies.txt -X POST http://localhost:8080/account/login \
  -d "login=admin&password=admin"

# 2. Create an API key
curl -b cookies.txt -X POST http://localhost:8080/api/keys \
  -H "Content-Type: application/json" \
  -d '{"name":"my-key","expiresInDays":365}'
# → {"key":"obx_...","name":"my-key","expiresAt":"..."}

# 3. Use the key for all subsequent requests
export OBX_KEY="obx_..."
```

### Using an API Key

```bash
# Create a bucket
curl -X POST "http://localhost:8080/api/buckets?name=my-bucket" \
  -H "X-API-Key: $OBX_KEY"

# Upload an object
curl -X PUT http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY" \
  --data-binary "Hello, ObjeX!"

# Download it
curl http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY"

# List objects
curl http://localhost:8080/api/objects/my-bucket/ \
  -H "X-API-Key: $OBX_KEY"

# Delete an object
curl -X DELETE http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY"

# Delete a bucket
curl -X DELETE http://localhost:8080/api/buckets/my-bucket \
  -H "X-API-Key: $OBX_KEY"
```

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│              ASP.NET Core 10 App                │
│                                                 │
│  ├─ Blazor Server UI (/)                        │
│  ├─ REST API (/api/*)                           │
│  ├─ Auth endpoints (/account/login, /logout)    │
│  └─ Scalar API Docs (/scalar/v1)               │
│                                                 │
│  Auth pipeline:                                 │
│  Cookie ──┐                                     │
│           ├─→ UseAuthorization → endpoints      │
│  API Key ─┘                                     │
│                                                 │
│  ┌─────────────────────────────────────────┐    │
│  │  Storage                                │    │
│  │  ./data/blobs/  (content-addressed FS)  │    │
│  │  ./objex.db     (SQLite — metadata +    │    │
│  │                  identity + job store)  │    │
│  └─────────────────────────────────────────┘    │
└─────────────────────────────────────────────────┘
```

### Project Structure

```
ObjeX/
├── src/
│   ├── ObjeX.Api/              # ASP.NET Core host
│   │   ├── Auth/               # HangfireAuthorizationFilter, NoOpEmailSender
│   │   ├── Endpoints/          # BucketEndpoints, ObjectEndpoints, ApiKeyEndpoints
│   │   ├── Middleware/         # ApiKeyAuthenticationMiddleware
│   │   └── Program.cs          # DI, middleware pipeline, EF migrations, admin seed
│   │
│   ├── ObjeX.Web/              # Blazor Server UI
│   │   └── Components/
│   │       ├── Pages/          # Dashboard, Buckets, Objects, Settings, Login
│   │       ├── Dialogs/        # Create/upload/API key dialogs
│   │       └── Layout/         # MainLayout (auth gate), NavMenu, EmptyLayout
│   │
│   ├── ObjeX.Core/             # Domain — no framework dependencies
│   │   ├── Interfaces/         # IMetadataService, IObjectStorageService, IHashService
│   │   ├── Models/             # Bucket, BlobObject, ApiKey, User
│   │   └── Validation/         # BucketNameValidator
│   │
│   └── ObjeX.Infrastructure/   # Implementations
│       ├── Data/               # ObjeXDbContext (IdentityDbContext<User>)
│       ├── Hashing/            # Sha256HashService
│       ├── Jobs/               # CleanupOrphanedBlobsJob (Hangfire)
│       ├── Metadata/           # SqliteMetadataService
│       ├── Migrations/         # EF Core migrations
│       └── Storage/            # FileSystemStorageService
```

---

## API Endpoints

All endpoints except `/account/*` and `/health` require authentication (`X-API-Key` header or session cookie).

### Buckets — `/api/buckets`

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/buckets` | List all buckets |
| `POST` | `/api/buckets?name={name}` | Create a bucket |
| `GET` | `/api/buckets/{name}` | Get bucket details |
| `DELETE` | `/api/buckets/{name}` | Delete a bucket |

Bucket name rules: 3–63 chars, lowercase alphanumeric and hyphens, no consecutive hyphens, cannot start/end with hyphen.

### Objects — `/api/objects/{bucketName}`

| Method | Path | Description |
|--------|------|-------------|
| `PUT` | `/api/objects/{bucket}/{*key}` | Upload an object (streaming) |
| `GET` | `/api/objects/{bucket}/{*key}` | Download an object |
| `DELETE` | `/api/objects/{bucket}/{*key}` | Delete an object |
| `GET` | `/api/objects/{bucket}/` | List objects in a bucket |

Object keys support slashes (virtual folders): `PUT /api/objects/my-bucket/images/photo.jpg`

Upload response:
```json
{ "key": "hello.txt", "etag": "a1b2c3...", "size": 13 }
```

### API Keys — `/api/keys`

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/keys` | Create a new API key |
| `GET` | `/api/keys` | List your API keys (no key value) |
| `DELETE` | `/api/keys/{id}` | Delete an API key |

Create request: `{"name":"my-key","expiresInDays":365}` (omit for 10-year default)
Create response: `{"key":"obx_...","name":"...","expiresAt":"..."}` — **key value shown once only**

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core 10 |
| API | Minimal APIs |
| UI | Blazor Server (Interactive SSR) + Radzen Blazor |
| API Docs | Scalar + OpenAPI |
| Auth | ASP.NET Core Identity (cookies) + custom API key middleware |
| Database | SQLite via EF Core 10 (snake_case cols, auto-migrated) |
| Blob store | Filesystem, content-addressable SHA256 paths |
| Background jobs | Hangfire (SQLite-backed, dashboard at `/hangfire`) |
| Logging | Serilog (console + request logging) |
| Compression | Response compression (HTTPS-enabled) |

---

## Configuration

No config required for local dev. Defaults:

| Setting | Default |
|---------|---------|
| Port | `http://localhost:8080` |
| Database | `<solution-root>/objex.db` |
| Blob storage | `<solution-root>/data/blobs/` |
| Admin username | `admin` |
| Admin email | `admin@objex.local` |
| Admin password | `admin` |

Override via `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/objex.db"
  },
  "Storage": {
    "BasePath": "/data/blobs"
  },
  "DefaultAdmin": {
    "Username": "myadmin",
    "Email": "admin@example.com",
    "Password": "changeme"
  }
}
```

### Blob Layout on Disk

Blobs use **content-addressable hashed paths** — the physical filename is a SHA256 hash of `"{bucketName}/{key}"`, spread across a 2-level directory tree:

```
/data/
├── blobs/
│   └── {bucket}/
│       └── {L1}/           # first 2 chars of SHA256 hash
│           └── {L2}/       # next 2 chars of SHA256 hash
│               └── {hash}.blob
└── objex.db                # SQLite — metadata + identity + Hangfire jobs
```

The logical key (e.g. `images/2024/photo.jpg`) lives in the database only.

---

## What's Implemented

- [x] Clean Architecture (Core / Infrastructure / API / Web)
- [x] Bucket CRUD with name validation
- [x] Object upload (streaming), download, delete, list
- [x] Blazor bucket detail page (`/buckets/{name}`) — file list, breadcrumb, upload dialog, per-object download + delete
- [x] ETag computation (MD5) on upload
- [x] SQLite metadata store via EF Core (auto-migrated on startup)
- [x] Content-addressable filesystem blob store (SHA256 hashed paths, 2-level nesting)
- [x] Orphaned blob GC via Hangfire background job (weekly, results in dashboard)
- [x] ASP.NET Core Identity — User model, password hashing, roles (Admin/User)
- [x] Default admin seeded on first run
- [x] Login/logout UI (Blazor + Radzen, username or email, toast on error)
- [x] Global Blazor route protection (all pages require login)
- [x] Cookie auth for browser sessions
- [x] API key auth for external clients (`X-API-Key` header, `obx_` prefix keys)
- [x] API key management UI (Settings page — create, list, delete)
- [x] Proper 401 responses for unauthenticated API requests (not 302 redirects)
- [x] Hangfire dashboard at `/hangfire` (Admin role required)
- [x] Scalar interactive API docs at `/scalar/v1`
- [x] Health check endpoint at `/health`
- [x] Serilog structured logging + request logging
- [x] Response compression

---

## Roadmap

See [ROADMAP.md](./ROADMAP.md) for the full plan.

- [x] **Content-addressable storage** — SHA256 hashed blob paths, orphaned blob GC
- [x] **Blazor UI** — dashboard (`/`), bucket list (`/buckets`), bucket detail + file browser (`/buckets/{name}`), drag-drop upload, per-object download/delete, API key management (`/settings`)
- [x] **Authentication** — Identity, cookie + API key dual auth, login/logout UI, admin seeding
- [x] **API Key system** — `X-API-Key` middleware, key management endpoints + UI
- [ ] **Dockerize** — Dockerfile + docker-compose, multi-arch (amd64/arm64)
- [ ] **S3 Compatibility** — AWS Sig V4, XML responses, aws-cli/boto3 support
- [ ] **Multipart Upload** — 5GB+ files, Initiate/UploadPart/Complete
- [ ] **Presigned URLs** — HMAC-SHA256 signed download + upload links
- [ ] **Enhanced Blazor UI** — previews, bulk ops, folder nav, dark mode
- [ ] **User Management UI** — registration, user list, password reset
- [ ] **Bucket Permissions** — per-bucket ACL, per-user read/write/delete
- [ ] **Teams & Organizations** — multi-tenant, quotas, team roles
- [ ] **Storage backends** — swap `FileSystemStorageService` for cloud storage
- [ ] **PostgreSQL support** — swap SQLite via `IMetadataService` interface

---

## References

- [MinIO](https://github.com/minio/minio) — patterns reference
- [SeaweedFS](https://github.com/seaweedfs/seaweedfs) — simpler distributed architecture
- [Garage](https://garagehq.deuxfleurs.fr/) — Rust-based self-hosted object storage
- [AWS S3 API Reference](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)
