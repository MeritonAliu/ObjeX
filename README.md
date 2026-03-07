# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite + Filesystem storage
**Status**: Active development — core API implemented, UI in progress

---

## Quick Start

```bash
# Clone and run
git clone https://github.com/youruser/ObjeX.git
cd ObjeX/src/ObjeX.Api
dotnet run
```

The app starts at **http://localhost:8080**

- **API docs (Scalar)**: http://localhost:8080/scalar/v1
- **Blazor UI**: http://localhost:8080
- **Health check**: http://localhost:8080/health

---

## Try It Out

```bash
# Create a bucket
curl -X POST "http://localhost:8080/api/buckets?name=my-bucket"

# Upload an object
curl -X PUT http://localhost:8080/my-bucket/hello.txt \
  --data "Hello, ObjeX!"

# Download it
curl http://localhost:8080/my-bucket/hello.txt

# List objects in a bucket
curl http://localhost:8080/my-bucket/

# List all buckets
curl http://localhost:8080/api/buckets

# Delete an object
curl -X DELETE http://localhost:8080/my-bucket/hello.txt

# Delete a bucket
curl -X DELETE http://localhost:8080/api/buckets/my-bucket
```

---

## Architecture

```
┌─────────────────────────────────────┐
│         ASP.NET Core 10 App         │
│                                     │
│  ├─ Minimal API (/api/*, /{bucket}) │
│  ├─ Blazor Server UI (/)            │
│  └─ Scalar API Docs (/scalar/v1)    │
│                                     │
│  ┌───────────────────────────────┐  │
│  │   Storage Layer               │  │
│  │   ./data/blobs/  (filesystem) │  │
│  │   ./objex.db     (SQLite)     │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

### Project Structure

```
ObjeX/
├── src/
│   ├── ObjeX.Api/              # ASP.NET Core host
│   │   ├── Endpoints/          # BucketEndpoints, ObjectEndpoints
│   │   └── Program.cs          # DI, middleware, EF migrations
│   │
│   ├── ObjeX.Web/              # Blazor Server UI (in progress)
│   │
│   ├── ObjeX.Core/             # Domain — no framework dependencies
│   │   ├── Interfaces/         # IMetadataService, IObjectStorageService
│   │   ├── Models/             # Bucket, BlobObject
│   │   └── Validation/         # BucketNameValidator
│   │
│   └── ObjeX.Infrastructure/   # Implementations
│       ├── Data/               # ObjeXDbContext (EF Core + SQLite)
│       ├── Metadata/           # SqliteMetadataService
│       ├── Migrations/         # EF Core migrations
│       └── Storage/            # FileSystemStorageService
│
└── README.md
```

---

## API Endpoints

### Buckets — `/api/buckets`

| Method   | Path                      | Description           |
|----------|---------------------------|-----------------------|
| `GET`    | `/api/buckets`            | List all buckets      |
| `POST`   | `/api/buckets?name={name}`| Create a bucket       |
| `GET`    | `/api/buckets/{name}`     | Get bucket details    |
| `DELETE` | `/api/buckets/{name}`     | Delete a bucket       |

Bucket name validation: 3–63 chars, lowercase alphanumeric and hyphens, no consecutive hyphens, cannot start/end with hyphen.

### Objects — `/{bucketName}`

| Method   | Path                    | Description                          |
|----------|-------------------------|--------------------------------------|
| `PUT`    | `/{bucket}/{*key}`      | Upload an object (streaming)         |
| `GET`    | `/{bucket}/{*key}`      | Download an object                   |
| `DELETE` | `/{bucket}/{*key}`      | Delete an object                     |
| `GET`    | `/{bucket}/`            | List objects in a bucket             |

Object keys support slashes (virtual folders): `PUT /my-bucket/images/photo.jpg`

Upload response:
```json
{ "key": "hello.txt", "etag": "a1b2c3...", "size": 13 }
```

---

## Technology Stack

| Layer       | Technology                              |
|-------------|-----------------------------------------|
| Runtime     | .NET 10, ASP.NET Core 10                |
| API         | Minimal APIs                            |
| UI          | Blazor Server (Interactive SSR)         |
| API Docs    | Scalar + OpenAPI                        |
| Database    | SQLite via EF Core 10 (snake_case cols) |
| Blob store  | Filesystem, content-addressable SHA256 paths (`FileSystemStorageService`) |
| Logging     | Serilog (console)                       |
| Compression | Response compression (HTTPS-enabled)    |

---

## Configuration

By default, no config is required. Defaults:

| Setting              | Default                                    |
|----------------------|--------------------------------------------|
| Port                 | `http://localhost:8080`                    |
| Database             | `<solution-root>/objex.db`                 |
| Blob storage path    | `<solution-root>/data/blobs/`              |

Override via `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/objex.db"
  },
  "Storage": {
    "BasePath": "/data/blobs"
  }
}
```

### File Layout on Disk

Blobs are stored with **content-addressable hashed paths** — the physical filename is a SHA256 hash of `"{bucketName}/{key}"`, spread across a 2-level directory tree:

```
/data/
├── blobs/
│   └── {bucket}/
│       └── {L1}/           # first 2 chars of SHA256 hash
│           └── {L2}/       # next 2 chars of SHA256 hash
│               └── {hash}.blob
└── objex.db                # SQLite — buckets + object metadata
```

The logical key (e.g. `images/2024/photo.jpg`) is stored in the database only — virtual folder paths have no filesystem representation.

---

## What's Implemented

- [x] Clean Architecture (Core / Infrastructure / API / Web separation)
- [x] Bucket CRUD with name validation
- [x] Object upload (streaming), download, delete, list
- [x] ETag computation (MD5) on upload
- [x] SQLite metadata store via EF Core (auto-migrated on startup)
- [x] Content-addressable filesystem blob store (SHA256 hashed paths, 2-level directory nesting)
- [x] Orphaned blob GC (`CleanupOrphanedBlobsAsync`)
- [x] Scalar interactive API docs
- [x] Health check endpoint
- [x] Serilog structured logging
- [x] Response compression
- [x] Blazor Server UI — dashboard, bucket browser, drag-drop upload, download, delete

---

## Roadmap

See [ROADMAP.md](./ROADMAP.md) for the full prioritized plan with timelines.

- [x] **Content-addressable storage** — SHA256 hashed blob paths, 2-level directory nesting, orphaned blob GC
- [x] **Blazor UI** — Radzen dashboard, bucket browser, drag-drop upload, download, delete
- [ ] **Dockerize** — Dockerfile + docker-compose, multi-arch (amd64/arm64)
- [ ] **API Key Auth** — `X-API-Key` header middleware, key management in DB
- [ ] **Object listing with prefix/delimiter** — virtual folder navigation
- [ ] **S3 Compatibility** — AWS Sig V4, XML responses, aws-cli/boto3/s3cmd support
- [ ] **Multipart Upload** — Initiate/UploadPart/Complete, 5GB+ support
- [ ] **Presigned URLs** — HMAC-SHA256 signed download + upload links
- [ ] **Enhanced Blazor UI** — previews, bulk ops, dark mode, analytics charts
- [ ] **Object Tags** — key-value tags, lifecycle/retention policies
- [ ] **User Authentication** — ASP.NET Core Identity, roles, JWT
- [ ] **Bucket Permissions** — per-bucket ACL, per-user read/write/delete
- [ ] **Teams & Organizations** — multi-tenant, quotas, team roles
- [ ] **Storage backends** — swap `FileSystemStorageService` for cloud or chunked storage
- [ ] **PostgreSQL support** — swap SQLite via same `IMetadataService` interface

---

## References

- [MinIO](https://github.com/minio/minio) — patterns reference
- [SeaweedFS](https://github.com/seaweedfs/seaweedfs) — simpler distributed architecture
- [Garage](https://garagehq.deuxfleurs.fr/) — Rust-based self-hosted object storage
- [AWS S3 API Reference](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)
