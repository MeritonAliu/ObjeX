<img src="src/ObjeX.Web/wwwroot/favicon.svg" width="48" alt="ObjeX" />

# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite + Filesystem storage
**Status**: Active development — core API, auth, and UI implemented

> **Scope:** Single-node object storage for homelabs, internal tools, and dev/test environments. Not yet suitable for mission-critical data — no replication, high availability, or point-in-time recovery.

---

## Quick Start

Docker image: [`ghcr.io/centrolabs/objex`](https://github.com/centrolabs/ObjeX/pkgs/container/objex)

```bash
git clone https://github.com/centrolabs/ObjeX.git
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

### Roles

| Role | Access |
|------|--------|
| **Admin** | Everything — user management, role promotion, all buckets, Hangfire, presigned URL settings |
| **Manager** | Users page, Settings (incl. presigned URLs), all buckets — cannot change roles |
| **User** | Own buckets only, S3 credentials, dark mode |

The seeded `admin` account is permanent and cannot be deleted or demoted. Create users via **Users → New User** (Admin or Manager only).

### S3 Credentials

Create credentials in **Settings → S3 Credentials**. The secret access key is shown once on creation — save it.

Point any S3-compatible client (AWS CLI, AWS SDKs, rclone, s3cmd, etc.) at `http://localhost:9000` with your access key and secret.

---

## API Endpoints

All endpoints on port 9001 except `/account/*` and `/health/*` require a session cookie. Port 9000 (S3 API) requires AWS Signature V4.

### Internal Endpoints — port `9001`

Used by the Blazor UI. Bucket/object CRUD is handled entirely through the S3 API on port 9000 — the Blazor UI calls services directly via DI, so no REST endpoints are needed for those.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/objects/{bucket}/{*key}` | Download an object (browser file download) |
| `GET` | `/api/objects/{bucket}/download` | Download objects as ZIP — accepts `?prefix=` to scope to a folder |
| `GET` | `/api/presign/{bucket}/{*key}` | Generate a presigned URL — accepts `?expires=N` (seconds) |
| `POST` | `/account/login` | Form login (sets cookie), redirects to `returnUrl` |
| `GET` | `/account/logout` | Clears cookie, redirects to `/login` |
| `GET` | `/health` | Liveness check (200 if process is up) |
| `GET` | `/health/ready` | Readiness check (DB connectivity + blob storage writability) |

### S3-Compatible API — port `9000`

Exposed on a dedicated port for drop-in compatibility with S3 clients (`aws-cli`, `boto3`, `s3cmd`, AWS SDK, etc.). Auth is **AWS Signature Version 4** — create credentials in Settings → S3 Credentials.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | List all buckets (S3 XML) |
| `HEAD` | `/{bucket}` | Bucket exists check |
| `PUT` | `/{bucket}` | Create bucket |
| `DELETE` | `/{bucket}` | Delete bucket |
| `PUT` | `/{bucket}/{*key}` | Upload object |
| `PUT` | `/{bucket}/{*key}?partNumber=N&uploadId=X` | Upload part (multipart) |
| `GET` | `/{bucket}/{*key}` | Download object (`?download=true` forces attachment); Range requests supported |
| `GET` | `/{bucket}/{*key}?uploadId=X` | List parts |
| `HEAD` | `/{bucket}/{*key}` | Object metadata |
| `DELETE` | `/{bucket}/{*key}` | Delete object |
| `DELETE` | `/{bucket}/{*key}?uploadId=X` | Abort multipart upload |
| `POST` | `/{bucket}/{*key}?uploads` | Initiate multipart upload |
| `POST` | `/{bucket}/{*key}?uploadId=X` | Complete multipart upload |

Configure `S3:PublicUrl` in `appsettings.json` (default `http://localhost:9000`) — used by presigned URL generation and the Blazor UI.

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
| Presigned URL default expiry | `3600` seconds (1 hour) — configurable in **Settings → Presigned URLs** |
| Presigned URL max expiry | `604800` seconds (7 days) — configurable in **Settings → Presigned URLs**; hard cap enforced server-side |
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

### Database

SQLite by default — zero config, no separate process. Future versions will support plugging in your own database (PostgreSQL, etc.) via the compose file.

If you're hitting SQLite limits: [sqlite.org/limits.html](https://sqlite.org/limits.html) · [WAL mode](https://sqlite.org/wal.html) · [when to use SQLite](https://sqlite.org/whentouse.html)

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

## CI/CD

### CI — `.github/workflows/ci.yml`

Build gate on push to `main` and all PRs — restore → build Release → fail fast on compile errors. No tests yet.

### CD — `.github/workflows/cd.yml`

On push to `main`: builds multi-arch image (amd64/arm64) and pushes to GitHub Container Registry automatically.

Published at [`ghcr.io/centrolabs/objex`](https://github.com/centrolabs/ObjeX/pkgs/container/objex):

```bash
docker pull ghcr.io/centrolabs/objex:latest
docker compose up -d
```

### Dependabot — `.github/dependabot.yml`

Weekly Monday PRs for NuGet packages (grouped: `radzen`, `ef-core`, `hangfire`, `serilog`) and GitHub Actions versions.

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
