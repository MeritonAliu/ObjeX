<img src="src/ObjeX.Web/wwwroot/favicon.svg" width="48" alt="ObjeX" />

# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite or PostgreSQL + Filesystem storage
**Status**: Active development — core API, auth, and UI implemented

> **Scope:** Single-node object storage for homelabs, internal tools, and dev/test environments. Not yet suitable for mission-critical data — no replication, high availability, or point-in-time recovery.

---

## Quick Start

Docker image: [`ghcr.io/centrolabs/objex`](https://github.com/centrolabs/ObjeX/pkgs/container/objex)

```bash
# Docker
docker pull ghcr.io/centrolabs/objex:latest
docker compose up -d

# Docker with PostgreSQL
docker compose -f docker-compose.postgres.yml up -d

# Kubernetes
helm install objex ./charts/objex

# From source
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
<img width="800" height="496" alt="grafik" src="https://github.com/user-attachments/assets/8cadcd71-de33-4554-a5a0-320362b35e68" />

---

## Authentication

ObjeX uses two auth mechanisms on separate ports:

| Port | Used by | Auth mechanism |
|------|---------|----------------|
| `9001` | Browser / Blazor UI | Cookie (ASP.NET Core Identity) |
| `9000` | S3 clients, SDKs, CLI | AWS Signature Version 4 |

> **Public exposure:** Only port `9000` (S3 API) needs to be publicly reachable — expose it via reverse proxy or tunnel (e.g. `s3.example.com`). Port `9001` (admin UI) should stay on your internal network. Configure client apps with the public S3 URL as their endpoint so both server-side SDK calls and browser presigned URLs use the same hostname.

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
| `GET` | `/metrics` | Prometheus metrics (HTTP stats + per-bucket storage gauges); requires `Metrics:Enabled=true` |

### S3-Compatible API — port `9000`

Exposed on a dedicated port for drop-in compatibility with S3 clients (`aws-cli`, `boto3`, `s3cmd`, AWS SDK, etc.). Auth is **AWS Signature Version 4** — create credentials in Settings → S3 Credentials.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | List all buckets (S3 XML) |
| `HEAD` | `/{bucket}` | Bucket exists check |
| `GET` | `/{bucket}?location` | Get bucket location (returns `us-east-1`) |
| `GET` | `/{bucket}?uploads` | List active multipart uploads |
| `PUT` | `/{bucket}` | Create bucket |
| `DELETE` | `/{bucket}` | Delete bucket |
| `PUT` | `/{bucket}/{*key}` | Upload object; supports `x-amz-copy-source` for server-side copy and `x-amz-meta-*` custom metadata |
| `PUT` | `/{bucket}/{*key}?partNumber=N&uploadId=X` | Upload part (multipart) |
| `GET` | `/{bucket}/{*key}` | Download object (`?download=true` forces attachment); Range requests supported |
| `GET` | `/{bucket}/{*key}?uploadId=X` | List parts |
| `HEAD` | `/{bucket}/{*key}` | Object metadata (includes `x-amz-meta-*` headers) |
| `DELETE` | `/{bucket}/{*key}` | Delete object |
| `POST` | `/{bucket}?delete` | Batch delete objects (XML key list) |
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
| Database provider | `sqlite` — set `Database:Provider=postgresql` for Postgres |
| Database | `./data/db/objex.db` (SQLite default); set `ConnectionStrings:DefaultConnection` for Postgres |
| Blob storage | `./data/blobs` (relative to working directory) |
| Log files | `./data/logs/objex-YYYYMMDD.log` — daily rolling, 30 days retention, compact JSON |
| Auto-migrate | `true` — set `Database:AutoMigrate=false` to disable startup migrations |
| Max upload size | unlimited — set `Storage:MaxUploadBytes` (bytes) to cap per-upload size |
| Min free disk | `524288000` (500MB) — uploads rejected with 507 if free space drops below this; override via `Storage:MinimumFreeDiskBytes` |
| Presigned URL default expiry | `3600` seconds (1 hour) — configurable in **Settings → Presigned URLs** |
| Presigned URL max expiry | `604800` seconds (7 days) — configurable in **Settings → Presigned URLs**; hard cap enforced server-side |
| Storage quota (global default) | unlimited — configurable in **Settings → Storage Quotas**; applies to User role only; Admin/Manager unlimited by default |
| Storage quota (per-user) | unlimited — override per user on **Users** page; applies to any role when explicitly set |
| Prometheus metrics | `false` — set `Metrics:Enabled=true` to expose `/metrics` on port 9001 |
| Admin username | `admin` |
| Admin email | `admin@objex.local` |
| Admin password | `admin` |
| Seed buckets | *(none)* |
| Seed S3 credential | *(none)* |

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

### Seeding Buckets and S3 Credentials

ObjeX can pre-create buckets and an S3 credential on startup so integrations work without manual UI setup. Both are idempotent (skipped if already exists). See `docker-compose.yml` for a full example with comments.

| Setting | Env var | Description |
|---------|---------|-------------|
| Seed buckets | `Seed__Buckets` | Comma-separated list, owned by admin |
| S3 access key | `Seed__S3Credential__AccessKeyId` | You choose the key |
| S3 secret key | `Seed__S3Credential__SecretAccessKey` | You choose the secret |
| S3 credential name | `Seed__S3Credential__Name` | Display name (default: `seed-credential`) |

### Security

See [SECURITY.md](SECURITY.md) for vulnerability reporting and encryption guidance.

Login rate-limited to 5 attempts per 2 minutes per IP. Hangfire dashboard restricted to Admin role. Security headers set on all responses (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `HSTS`).

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

Build + test gate on push to `main` and all PRs — restore → build Release → run xUnit test suite.

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

111 automated tests (xUnit, ~5 seconds). Integration tests use real SQLite via `WebApplicationFactory` — no mocks.

```bash
dotnet test src/ObjeX.Tests/
```

**Integration-tested with:** [Outline](https://github.com/outline/outline) (presigned POST uploads + presigned GET retrieval) and [Memos](https://github.com/usememos/memos) (server-side PUT uploads + presigned GET retrieval) as S3 storage backends.

### What's covered

- S3 object lifecycle — upload, download, ETag verification, delete, 404 confirmation, range requests, custom metadata
- Bucket CRUD — create, list, head, delete, duplicate detection, non-empty delete rejection
- Multipart upload — initiate, upload parts, complete, download assembled file, abort cleanup
- Auth boundaries — no credentials, invalid key, wrong signature, expired timestamp, presigned URLs (valid + expired)
- Path traversal — `../`, `..\\`, encoded variants; all blobs verified within base path
- Storage quotas — per-user limits, global defaults, admin bypass, 507 on exceed
- Audit log — bucket and object mutations write entries
- Batch delete — multiple keys, mixed existing/non-existent
- CopyObject — within bucket, cross-bucket, non-existent source
- Role isolation — User sees only own buckets; Admin/Manager see all (via S3 API and Blazor)
- Cookie auth — login, bad password redirect, logout
- Health endpoints — liveness, readiness
- Security headers — X-Content-Type-Options, X-Frame-Options, Referrer-Policy, no Server header
- S3 compatibility — stub 501 for unimplemented ops, GetBucketLocation, ListMultipartUploads
- Resilience — missing blob file returns error (not 200), concurrent uploads maintain consistent state
- Core validators — BucketNameValidator, ObjectKeyValidator, HashingStream, Sha256HashService

### Known gaps

| Scenario | Status |
|----------|--------|
| Disk full during upload | ⚠️ Not tested under real disk pressure |
| DB locked under concurrent writes | ⚠️ No explicit retry policy tuning |
| Corrupt blob on read | ❌ No integrity check — ETag stored but not verified on download |
| Large file upload (500MB+) | ⚠️ Streaming behavior under memory pressure unknown |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## References

- [MinIO](https://github.com/minio/minio) — patterns reference
- [SeaweedFS](https://github.com/seaweedfs/seaweedfs) — simpler distributed architecture
- [Garage](https://garagehq.deuxfleurs.fr/) — Rust-based self-hosted object storage
- [AWS S3 API Reference](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)
