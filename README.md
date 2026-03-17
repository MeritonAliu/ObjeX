<img src="src/ObjeX.Web/wwwroot/favicon.svg" width="48" alt="ObjeX" />

# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite + Filesystem storage
**Status**: Active development — core API, auth, and UI implemented

> **Scope:** Single-node object storage for homelabs, internal tools, and dev/test environments. Not yet suitable for mission-critical data — no replication, high availability, or point-in-time recovery.

---

## Quick Start

```bash
git clone https://github.com/youruser/ObjeX.git
cd ObjeX/src/ObjeX.Api
dotnet run
```

Open **http://localhost:9001** — log in with `admin` / `admin`.

- **Blazor UI**: http://localhost:9001
- **Job dashboard**: http://localhost:9001/hangfire
- **Health check**: http://localhost:9001/health (liveness)
- **Health check (readiness)**: http://localhost:9001/health/ready

> ⚠️ Change the default admin credentials before exposing the instance publicly. Set `DefaultAdmin:Username`, `DefaultAdmin:Email`, and `DefaultAdmin:Password` in `appsettings.json` or environment variables.

---

## Authentication

ObjeX uses two auth mechanisms on separate ports:

| Port | Used by | Auth mechanism |
|------|---------|----------------|
| `9001` | Browser / Blazor UI | Cookie (ASP.NET Core Identity) |
| `9000` | S3 clients, SDKs, CLI | AWS Signature Version 4 |

### S3 Credentials

Create credentials in **Settings → S3 Credentials**. The secret access key is shown once on creation — save it.

```python
# boto3
import boto3
s3 = boto3.client(
    "s3",
    endpoint_url="http://localhost:9000",
    aws_access_key_id="OBXXXX...",
    aws_secret_access_key="your-secret",
)
s3.put_object(Bucket="my-bucket", Key="hello.txt", Body=b"Hello, ObjeX!")
```

```csharp
// AWS SDK for .NET
var client = new AmazonS3Client(
    "OBXXXX...", "your-secret",
    new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true });
await client.PutObjectAsync(new PutObjectRequest {
    BucketName = "my-bucket", Key = "hello.txt", ContentBody = "Hello, ObjeX!" });
```

```bash
# aws-cli
aws s3 --endpoint-url http://localhost:9000 \
  --aws-access-key-id OBXXXX... --aws-secret-access-key your-secret \
  cp hello.txt s3://my-bucket/hello.txt
```

---

## Architecture

```
Port 9001 — UI + native API
┌─────────────────────────────────────────────────┐
│              ASP.NET Core 10 App                │
│                                                 │
│  ├─ Blazor Server UI (/)                        │
│  ├─ REST API (/api/*)                           │
│  ├─ Auth endpoints (/account/login, /logout)    │
│  └─ Scalar API Docs (/scalar/v1)               │
│                                                 │
│  Auth: Cookie ──→ UseAuthorization              │
│                                                 │
│  ┌─────────────────────────────────────────┐    │
│  │  ./data/blobs/  (content-addressed FS)  │    │
│  │  ./data/db/objex.db  (SQLite)           │    │
│  └─────────────────────────────────────────┘    │
└─────────────────────────────────────────────────┘

Port 9000 — S3-compatible API
┌─────────────────────────────────────────────────┐
│  ├─ GET  /                  (list buckets)      │
│  ├─ HEAD/PUT/DELETE /{bucket}                   │
│  └─ PUT/GET/HEAD/DELETE /{bucket}/{*key}        │
│                                                 │
│  Auth: AWS Signature V4 (SigV4AuthMiddleware)   │
│    → parse → credential lookup → sig verify     │
│    → timestamp check → payload hash verify      │
└─────────────────────────────────────────────────┘
```

### Project Structure

```
ObjeX/
├── src/
│   ├── ObjeX.Api/              # ASP.NET Core host
│   │   ├── Auth/               # HangfireAuthorizationFilter
│   │   ├── Endpoints/          # BucketEndpoints, ObjectEndpoints, DownloadEndpoints
│   │   │   └── S3Endpoints/    # S3BucketEndpoints, S3ObjectEndpoints
│   │   ├── Middleware/         # SigV4AuthMiddleware
│   │   ├── S3/                 # SigV4Parser, SigV4Signer, S3Xml, S3Errors
│   │   └── Program.cs          # DI, middleware pipeline, EF migrations, admin seed
│   │
│   ├── ObjeX.Web/              # Blazor Server UI
│   │   └── Components/
│   │       ├── Pages/          # Dashboard, Buckets, Objects, Settings, Profile, Login
│   │       ├── Dialogs/        # Create/upload/S3 credential/folder dialogs
│   │       └── Layout/         # MainLayout (auth gate), NavMenu, EmptyLayout
│   │
│   ├── ObjeX.Core/             # Domain — no framework dependencies
│   │   ├── Interfaces/         # IMetadataService, IObjectStorageService, IHashService
│   │   ├── Models/             # Bucket, BlobObject, S3Credential, User
│   │   └── Validation/         # BucketNameValidator, ObjectKeyValidator
│   │
│   └── ObjeX.Infrastructure/   # Implementations
│       ├── Data/               # ObjeXDbContext (IdentityDbContext<User>)
│       ├── Hashing/            # Sha256HashService
│       ├── Jobs/               # CleanupOrphanedBlobsJob, VerifyBlobIntegrityJob (Hangfire)
│       ├── Metadata/           # SqliteMetadataService
│       ├── Migrations/         # EF Core migrations
│       └── Storage/            # FileSystemStorageService
```

---

## API Endpoints

All endpoints on port 9001 except `/account/*` and `/health/*` require a session cookie. Port 9000 (S3 API) requires AWS Signature V4.

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
| `GET` | `/api/objects/{bucket}/` | List objects — accepts `?prefix=&delimiter=`; returns `{ objects, commonPrefixes }` |
| `GET` | `/api/objects/{bucket}/download` | Download objects as ZIP — accepts `?prefix=` to scope to a folder |

Object keys support slashes (virtual folders): `PUT /api/objects/my-bucket/images/photo.jpg`

**Key validation:** keys are rejected with `400` if they are empty, exceed 1024 characters, start with `/`, or contain control characters (including null bytes). `..` segments and `\` are normalised on the storage path but the original key is stored as-is.

**Overwrite semantics:** `PUT` to an existing key silently overwrites — last write wins. The old blob becomes an orphan and is cleaned up by the weekly GC job. Safe to retry on network failure.

Upload response:
```json
{ "key": "hello.txt", "etag": "a1b2c3...", "size": 13 }
```

### S3-Compatible API — port `9000`

Exposed on a dedicated port for drop-in compatibility with S3 clients (`aws-cli`, `boto3`, `s3cmd`, AWS SDK, etc.). Auth is **AWS Signature Version 4** — create credentials in Settings → S3 Credentials.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | List all buckets (S3 XML) |
| `HEAD` | `/{bucket}` | Bucket exists check |
| `PUT` | `/{bucket}` | Create bucket |
| `DELETE` | `/{bucket}` | Delete bucket |
| `PUT` | `/{bucket}/{*key}` | Upload object |
| `GET` | `/{bucket}/{*key}` | Download object (`?download=true` forces attachment) |
| `HEAD` | `/{bucket}/{*key}` | Object metadata |
| `DELETE` | `/{bucket}/{*key}` | Delete object |

Configure `S3:PublicUrl` in `appsettings.json` (default `http://localhost:9000`) — used by the Blazor UI to build download links.

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core 10 |
| API | Minimal APIs |
| UI | Blazor Server (Interactive SSR) + Radzen Blazor |
| API Docs | Scalar + OpenAPI |
| Auth | ASP.NET Core Identity (cookies) + AWS Signature V4 (S3 port) |
| Database | SQLite via EF Core 10 (snake_case cols, auto-migrated) |
| Blob store | Filesystem, content-addressable SHA256 paths |
| Background jobs | Hangfire (SQLite-backed, dashboard at `/hangfire`) |
| Logging | Serilog (console + request logging) |
| Compression | Response compression (HTTPS-enabled) |

---

## Configuration

No config required for local dev. Defaults (from `appsettings.json`):

| Setting | Default |
|---------|---------|
| UI / API port | `9001` |
| S3 API port | `9000` (S3-compatible endpoints; AWS Signature V4 required) |
| S3 public URL | `http://localhost:9000` — set `S3:PublicUrl` for production |
| Database | `./data/db/objex.db` (relative to working directory) |
| Blob storage | `./data/blobs` (relative to working directory) |
| Log files | `./data/logs/objex-YYYYMMDD.log` — daily rolling, 30 days retention, compact JSON |
| Auto-migrate | `true` — set `Database:AutoMigrate=false` to disable startup migrations |
| Max upload size | unlimited — set `Storage:MaxUploadBytes` (bytes) to cap per-upload size |
| Min free disk | `524288000` (500MB) — uploads rejected with 507 if free space drops below this; override via `Storage:MinimumFreeDiskBytes` |
| Admin username | `admin` |
| Admin email | `admin@objex.local` |
| Admin password | `admin` |

Override via `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/opt/objex/data/db/objex.db"
  },
  "Storage": {
    "BasePath": "/opt/objex/data/blobs"
  },
  "DefaultAdmin": {
    "Username": "myadmin",
    "Email": "admin@example.com",
    "Password": "changeme"
  }
}
```

> ⚠️ Change default admin credentials before exposing the instance publicly.

### SQLite Limitations

SQLite is the right choice for single-node homelab use — zero config, no separate process, trivially backed up with `cp`. It becomes a bottleneck in specific scenarios:

**What works fine:**
- Personal or small-team use (handful of concurrent users)
- Bursty uploads — SQLite handles short write spikes well in WAL mode
- Read-heavy workloads — WAL mode allows concurrent readers with no lock contention

**What to watch out for:**
- **Sustained concurrent writes** — Hangfire polls every few seconds while EF Core writes on every upload/delete/key rotation. Under heavy parallel upload bursts this can produce `SQLITE_BUSY` retries and degraded throughput
- **Network filesystems** — do not host `objex.db` on NFS, SMB, or any network-mounted path. SQLite uses POSIX advisory locks which are unreliable over NFS and can cause silent database corruption
- **Not benchmarked** — no formal throughput testing has been done. If you need numbers, run your own load test against your hardware

**Auto-migration:** enabled by default (`Database:AutoMigrate=true`). A warning is logged before migrations run. For production, consider setting `Database:AutoMigrate=false` and running `dotnet ef database update` as a pre-deploy step — this gives you control over when schema changes apply and lets you take a backup first (see [Backup & Restore](#backup--restore)). EF Core migrations are idempotent so a restart loop won't compound damage, but a failed migration mid-deploy will block startup until fixed.

**Multi-instance:** startup migration (`db.Database.Migrate()`) is not safe for concurrent multi-instance deployments — if two processes start simultaneously, both race on schema migration. SQLite's file lock serializes this in practice but it's not a guarantee. ObjeX is single-node by design; if you ever run multiple instances, extract migrations into a dedicated pre-start step.

**SQLite configuration:** WAL mode (`journal_mode=WAL`), `synchronous=NORMAL`, and `busy_timeout=5000` are applied via PRAGMA on every startup and persist to the DB file. WAL enables concurrent reads during writes. `busy_timeout` makes SQLite retry internally for up to 5 seconds on lock contention before throwing `SQLITE_BUSY`. EF Core `CommandTimeout` is set to 30 seconds.

**Architecture note:** Hangfire, EF Core (metadata + Identity), and the app all share one `objex.db` file. Separating Hangfire onto its own SQLite file or an in-memory store is a future improvement. For now, the weekly cleanup job is the only significant Hangfire write activity.

**Upgrade path:** The `IMetadataService` interface is the only thing that needs a new implementation to swap SQLite for PostgreSQL. See roadmap.

### Backup & Restore

> **Current state:** no built-in backup tooling. Manual procedure only.

#### What needs to be backed up

ObjeX data lives in two places that must be backed up **together and consistently**:

```
data/
├── db/objex.db     # SQLite — all metadata, user accounts, API keys, bucket definitions
└── blobs/          # content-addressed blob files (SHA256-named .blob files)
```

The logical key → physical blob mapping exists **only in the database**. If you lose `objex.db` but keep the blobs, you have a directory of `a3f7c2....blob` files with no way to know which object each one represents. There is currently no tool to rebuild the index from disk.

#### Docker (recommended setup)

Data lives in a named Docker volume. To back it up:

```bash
# Stop the container first (ensures DB is not mid-write)
docker compose stop objex

# Copy the volume to a backup location
docker run --rm \
  -v objex_data:/data \
  -v /your/backup/path:/backup \
  alpine cp -a /data /backup/objex-$(date +%Y%m%d)

# Restart
docker compose start objex
```

Hot backup (without stopping) is possible using SQLite's online backup:
```bash
docker exec objex sqlite3 /data/db/objex.db ".backup /data/db/objex.db.bak"
```
Then copy the `.bak` file and the blobs. There is a small race window between the DB backup and the blob copy — any blobs written in that window will be orphaned and cleaned up by the weekly Hangfire GC job. No data loss, but a slightly inconsistent snapshot is possible.

#### Bare metal / direct deploy

```bash
# Stop the app first
pkill -f ObjeX.Api.dll

cp -a ~/objex/data/ ~/backups/objex-$(date +%Y%m%d)/

# Restart
dotnet ~/objex/ObjeX.Api.dll --urls "http://0.0.0.0:9001"
```

#### Restore

1. Stop the running instance
2. Replace `data/` with the backup copy
3. Start the instance — EF Core will validate the schema on startup
4. Hit `/health/ready` to confirm DB connectivity and blob storage are both healthy
5. Spot-check a few object downloads to verify blob integrity

#### Consistency guarantees

| Scenario | Outcome |
|----------|---------|
| DB newer than blobs | Object records exist with no backing blob → download returns 404 |
| Blobs newer than DB | Orphaned blobs → cleaned up automatically by weekly Hangfire GC |
| Both from same stopped snapshot | Fully consistent |

### Encryption

ObjeX does not encrypt blobs or metadata at the application level. For data at rest, rely on full-disk encryption at the host (e.g. LUKS, BitLocker, or encrypted cloud volumes). For data in transit, run ObjeX behind a TLS-terminating reverse proxy (nginx, Caddy, Traefik).

### HTTP Security Headers

ObjeX sets the following headers on every response:

| Header | Value |
|--------|-------|
| `Server` | *(removed — Kestrel default suppressed via `AddServerHeader = false`)* |
| `X-Powered-By` | *(removed)* |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `X-Permitted-Cross-Domain-Policies` | `none` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` (non-dev only) |

**Hangfire dashboard** (`/hangfire`) is publicly routable but protected by Admin role (localhost bypasses auth for dev convenience). The dashboard exposes job history, parameters, and retry controls — sufficient for a self-hosted admin tool. If you want to restrict it further (e.g. internal network only), block it at the reverse proxy: `location /hangfire { deny all; }`. ObjeX's current jobs carry no sensitive parameters; take care if you add jobs that do.

Content Security Policy (CSP) is not yet set — Blazor Server requires inline scripts and a `ws://` WebSocket connection for SignalR, making a safe policy non-trivial. Deferred to a future hardening pass.

**Rate limiting:** `POST /account/login` is limited to 5 attempts per 2 minutes per IP (sliding window) — returns 429 when exceeded. `POST /api/keys` is limited to 10 per minute per IP. Rate limiting is IP-based via `RemoteIpAddress` — if you run ObjeX behind a reverse proxy, ensure `X-Forwarded-For` is correctly forwarded, otherwise the limiter sees the proxy IP and may block all users simultaneously.

**CORS:** ObjeX allows any origin (`AllowAnyOrigin`). This is intentional for self-hosted use where the origin isn't known upfront. Browsers block `AllowAnyOrigin` + `AllowCredentials` simultaneously, so cookie sessions are safe. If you add `AllowCredentials()` in the future, you must also restrict to explicit origins — the wildcard + credentials combination is rejected by browsers and would need to be replaced.

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

## Roadmap

See [ROADMAP.md](./ROADMAP.md).

---

## CI

### GitHub Actions (`.github/workflows/ci.yml`)

Build-only gate on push to `main` and all PRs — restore → build Release → fail fast on compile errors. No tests yet.

### Dependabot (`.github/dependabot.yml`)

Weekly Monday PRs for NuGet packages (grouped: `radzen`, `ef-core`, `hangfire`, `serilog`) and GitHub Actions versions.

### Docker Hub

Published at [`meritonaliu/objex`](https://hub.docker.com/r/meritonaliu/objex) — multi-arch (amd64/arm64):

```bash
docker pull meritonaliu/objex:latest
docker compose up -d
```

## Testing

**Current state:** CI is build-only — no automated tests exist yet. The scenarios below are the known gaps before ObjeX can be considered production-ready.

### Hostile Scenario Coverage

| Scenario | Status | How it's handled |
|----------|--------|-----------------|
| Power loss mid-upload | ✅ Handled | Atomic write: `.tmp` → `File.Move`; stale `.tmp` cleaned on startup |
| Crash between blob write and metadata commit | ✅ Handled | Orphaned blob cleaned by weekly Hangfire GC |
| Path traversal in object key (`../../../etc/passwd`) | ✅ Handled | `SanitizeKey` strips `..` and normalises `\` → `/`; hashed paths never touch filesystem raw |
| Invalid/expired S3 credential | ✅ Handled | SigV4AuthMiddleware returns S3 XML error (403) |
| Missing blob file with valid metadata | ✅ Handled | `RetrieveAsync` throws `FileNotFoundException` → 404 |
| Disk full during upload | ⚠️ Partially handled | `.tmp` write fails and is cleaned up; API returns 500 — not tested under real disk pressure |
| Two concurrent uploads to same key | ⚠️ Untested | `File.Move(overwrite: true)` is atomic on Linux; DB upsert behavior under race not validated |
| DB locked under concurrent writes | ⚠️ Untested | EF Core retries on `SQLITE_BUSY`; no explicit retry policy or timeout tuning |
| Corrupt blob file with valid metadata | ❌ Not handled | Download returns corrupt bytes with 200 — no integrity check on read (ETag is stored but not verified) |
| Backup and restore drill | ❌ Not tested | Procedure documented; never actually drilled end-to-end |
| Large file upload (500MB+) | ⚠️ Untested | Blazor hub limit set to 500MB; streaming behavior under memory pressure unknown |
| Delete non-existent object | ✅ Handled | Idempotent — `File.Delete` is no-op if missing; DB delete is a no-op on missing row |
| Upload with no `Content-Type` header | ✅ Handled | Stored as `application/octet-stream` fallback |

### What needs automated tests

- Integration tests hitting a real SQLite DB (not mocked)
- Upload → download round-trip with ETag verification
- Concurrent upload stress test (same key, different keys)
- Auth boundary tests (no key, expired key, wrong key, valid cookie vs API key)
- Path traversal fuzzing on object keys
- Fault injection: disk full simulation, corrupted blob detection

---

## References

- [MinIO](https://github.com/minio/minio) — patterns reference
- [SeaweedFS](https://github.com/seaweedfs/seaweedfs) — simpler distributed architecture
- [Garage](https://garagehq.deuxfleurs.fr/) — Rust-based self-hosted object storage
- [AWS S3 API Reference](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)
