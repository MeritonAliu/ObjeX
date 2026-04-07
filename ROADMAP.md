# ObjeX Roadmap

---

## v0.1.0 — Foundation

### Content-Addressable Blob Storage
- `IHashService` interface in `ObjeX.Core` (BCL only, no NuGet)
- `Sha256HashService` in `ObjeX.Infrastructure/Hashing/` — SHA256 of `"{bucket}/{key}"`, 64-char lowercase hex
- `FileSystemStorageService` stores blobs at `{basePath}/{bucket}/{L1}/{L2}/{hash}.blob`
  - L1/L2 = first 4 hex chars → 65,536 directories, even distribution
  - Logical key (virtual path) lives in DB only — no filesystem folders for virtual paths
- `IHashService` registered as singleton in `Program.cs`

### Atomic Blob Writes
- `FileSystemStorageService.StoreAsync` writes to `{hash}.blob.tmp` then `File.Move(..., overwrite: true)` — atomic on Linux
- On exception, `.tmp` file deleted in `catch` — final path never touched
- Startup cleanup: `.tmp` files older than 1 hour deleted automatically, count logged

### Authentication & Authorization
- **ASP.NET Core Identity** — `User` extends `IdentityUser`, `ObjeXDbContext` extends `IdentityDbContext<User>`
- **Roles** — `Admin` and `User` seeded on first startup
- **Default admin** — `admin` / `admin` (configurable via `DefaultAdmin:*` config keys; warned in logs)
- **Login UI** — `/login` page with Radzen styling, username-or-email support, error toast, auth guard (redirects to `/` if already logged in)
- **Logout** — `GET /account/logout` clears cookie, redirects to `/login`
- **Global Blazor protection** — `AuthorizeView` in `MainLayout.razor` wraps all pages; `Login.razor` uses `EmptyLayout` to opt out
- **Cookie auth for browser** — default scheme is `Identity.Application`; challenges redirect to `/login`
- **AWS Signature V4 for S3 clients** — `SigV4AuthMiddleware` on port 9000; validates HMAC-SHA256 canonical request, timestamp freshness (±15 min), payload hash; presigned URL expiry enforced via `X-Amz-Expires`
- **S3 credentials model** — `S3Credential` in `ObjeX.Core/Models/`; `AccessKeyId` ("OBX" + 17 random chars) + `SecretAccessKey` (40-byte base64url, stored plain for HMAC); `Create()` factory returns secret once
- **S3 credentials UI** — Settings page lists credentials, create (one-time secret display with copy button), delete
- **Proper 401 for API paths** — `ConfigureApplicationCookie` suppresses redirect for `/api/*`; `UseStatusCodePagesWithRedirects` skipped for `/api/*`
- **Hangfire dashboard** — `HangfireAuthorizationFilter` allows localhost unconditionally; requires `Admin` role otherwise

### S3 API Compatibility
- [x] S3-style routes on dedicated port 9000: `GET /`, `HEAD/PUT/DELETE /{bucket}`, `PUT/GET/HEAD/DELETE /{bucket}/{*key}`
- [x] XML response formatting via `S3Xml` helper (`SecurityElement.Escape()` for injection prevention)
- [x] S3 error code constants (`S3Errors` class)
- [x] `?download=true` query param forces `application/octet-stream` attachment (cross-origin download fix)
- [x] AWS Signature V4 authentication — `SigV4Parser`, `SigV4Signer`, `SigV4AuthMiddleware`; canonical request, timestamp replay protection, presigned URL expiry, payload hash verification
- [x] `aws-chunked` streaming — `STREAMING-*` payload hash bypassed in `SigV4AuthMiddleware`; outer request signature still verified
- [x] `ListObjects` (V1) and `ListObjectsV2` (`?list-type=2`) with prefix + delimiter support; `continuation-token` and `start-after` echoed back for client compatibility
- [x] S3 error response XML format for all error cases — all errors via `S3Xml.Error()` consistently
- [x] Compatibility testing with `aws-cli` — verified uploads, downloads, multipart, presigned URLs

### Multipart Upload
- [x] `POST /{bucket}/{key}?uploads` — InitiateMultipartUpload
- [x] `PUT /{bucket}/{key}?partNumber={n}&uploadId={id}` — UploadPart
- [x] `POST /{bucket}/{key}?uploadId={id}` — CompleteMultipartUpload
- [x] `DELETE /{bucket}/{key}?uploadId={id}` — AbortMultipartUpload
- [x] Temporary part storage under `_multipart/{uploadId}/{partNumber}.part`
- [x] Part ETag tracking (MD5 per part, multipart ETag format `md5-{partCount}`)
- [x] Atomic final object assembly (tmp → move)
- [x] 5 GB+ file support (no size limit beyond disk space)
- [x] Range request support — `enableRangeProcessing: true` fixes AWS CLI parallel downloads
- [x] Weekly Hangfire job cleans abandoned uploads older than 7 days (`CleanupAbandonedMultipartJob`)

### Presigned URLs
- [x] HMAC-SHA256 signed GET URLs with configurable expiry
- [x] Expiry enforcement on every request (`X-Amz-Expires` in `SigV4AuthMiddleware`)
- [x] `PresignedUrlGenerator` in `ObjeX.Core.Utilities` — pure BCL, usable from both Api and Web
- [x] `GET /api/presign/{bucket}/{*key}?expires=N` endpoint (cookie auth, port 9001)
- [x] Copy-link button in Blazor UI — opens duration picker dialog, copies URL to clipboard
- [x] Duration picker: quick-select chips + custom number/unit input, live expiry preview
- [x] Default and max expiry configurable via `S3:PresignedUrlDefaultExpirySeconds` / `S3:PresignedUrlMaxExpirySeconds`

### Blazor UI
- Radzen Blazor component library, Inter font (self-hosted), teal brand theme via CSS variable overrides
- **Dashboard** (`/`) — stat cards (buckets, objects, storage, avg size), storage/objects/file-type charts with tooltips, bucket table with share bars (10s auto-refresh)
- **Buckets page** (`/buckets`) — list all buckets, create (with inline name validation), delete with confirmation
- **Bucket detail / file browser** (`/buckets/{name}`) — virtual folder nav, breadcrumb, upload into current folder, per-object download (via S3 port 9000), delete, ZIP download
- **Drag-and-drop upload dialog** — multi-file picker, streams files to `IObjectStorageService` directly from Blazor (no API round-trip)
- **Settings page** (`/settings`) — S3 credential management (create with one-time secret display, list, delete) + dark/light mode toggle
- **Profile page** (`/profile`) — username, email, password management with inline validation
- Toast notifications bottom-right via Radzen `NotificationService`

### Virtual Folder Navigation
- `ListObjectsResult` record in `ObjeX.Core/Models/` — `Objects` + `CommonPrefixes`
- `IMetadataService.ListObjectsAsync` accepts optional `prefix` and `delimiter` — prefix filter pushed to DB (`LIKE`), delimiter grouping in C#
- API list endpoint accepts `?prefix=&delimiter=` query params; returns `{ objects, commonPrefixes }`
- `GET /api/objects/{bucket}/download?prefix=` — streams ZIP of all objects under a prefix
- Blazor `Objects.razor` — unified file+folder grid, breadcrumb, New Folder button, folder delete (recursive), folder ZIP download, upload into current prefix
- Placeholder objects (key ends with `/`, `ContentType: application/x-directory`) used for empty folders; filtered from file rows in UI

### Dark Mode
- System preference detected via `prefers-color-scheme` on first visit (inline `<script>` in `<head>`)
- Cookie `objex-theme` persists choice across sessions
- `App.razor` reads cookie server-side via `IHttpContextAccessor` → passes to `<RadzenTheme>` — no flash on load
- `ThemeService` (Radzen) registered as `AddScoped<ThemeService>()` — drives client-side switching
- Toggle (`RadzenSwitch`) in Settings page; initial state read from cookie via JS in `OnAfterRenderAsync`

### Hangfire Background Jobs
- `Hangfire.Core` + `Hangfire.AspNetCore` + `Hangfire.Storage.SQLite` (reuses `objex.db`)
- Job classes in `ObjeX.Infrastructure/Jobs/` — no job logic in API layer
- `CleanupOrphanedBlobsJob` — weekly Sunday 03:00 UTC, returns `CleanupResult` (checked/deleted/duration/timestamp) visible in Hangfire dashboard history
- `VerifyBlobIntegrityJob` — weekly Sunday 04:00 UTC, recomputes MD5 of every blob and compares against stored ETag; logs errors for corrupted/missing blobs; returns `IntegrityResult`
- Dashboard at `/hangfire` — Admin role required (localhost bypassed for dev)
- `IMetadataService.ListAllObjectsAsync()` added for cross-bucket object enumeration

### CI/CD
- **CI** (`ci.yml`) — triggers on push to `main` and all PRs; runs on `ubuntu-latest`; restore → build Release → run xUnit test suite
- **CD** (`cd.yml`) — triggers on push to `main`; builds multi-arch image (amd64/arm64) and pushes to GitHub Container Registry (`ghcr.io/centrolabs/objex:latest` + `ghcr.io/centrolabs/objex:<tag>`)

### Dockerize
- Multi-stage Dockerfile (SDK build → ASP.NET runtime)
- Multi-arch via `--platform=$BUILDPLATFORM` + `$TARGETARCH`
- `docker-compose.yml` with named volume for `/data`
- Environment variables for connection string and blob path baked into image defaults
- Published to GitHub Container Registry: `ghcr.io/centrolabs/objex:latest`

---

## v0.2.0 — User Management & S3 Extensions

- [x] S3 POST Object — browser-based uploads via presigned POST policy (form-field auth, `POST /{bucket}` + `POST /` bucketEndpoint mode)
- [x] DeleteObjects (batch delete) — `POST /{bucket}?delete` with XML key list; used by `aws s3 rm --recursive` and `aws s3 sync --delete`
- [x] Startup seeding of S3 credentials and buckets via config/env vars (zero-UI integration setup)
- [x] User management UI — Admin/Manager roles, user list, create/deactivate/delete/reset pw, forced password change on first login
- [x] Bucket ownership — buckets owned by creator; Admin/Manager see all; User sees own only; enforced at API, S3, and Blazor layers
- [x] ListObjectsV2 (`?list-type=2`) with continuation token support
- [x] Presigned PUT URL support
- [x] `x-amz-meta-*` custom metadata — stored as JSON on BlobObject, returned on GET/HEAD, captured on PUT/POST Object
- [x] CopyObject — `PUT /{bucket}/{*key}` with `x-amz-copy-source` header; copies blob + metadata server-side
- [x] ListMultipartUploads — `GET /{bucket}?uploads` lists active multipart uploads
- [x] Stub 501 responses for unsupported bucket operations (versioning, lifecycle, policy, cors, encryption, tagging, acl)
- [ ] POST Object: `${filename}` variable substitution in key — deferred (no modern SDK uses this)
- [ ] POST Object: `success_action_redirect` / `success_action_status` — deferred (all SDKs use default 204)
- [ ] PUT presigned URLs (direct browser upload without credentials) — deferred

---

## v1.0.0 — Production Ready (current)

- [x] Storage quota enforcement — per-user and global default, checked on all upload paths
- [x] Prometheus `/metrics` endpoint — HTTP request stats + per-bucket storage gauges via `prometheus-net`
- [x] Helm chart for Kubernetes deployment (`charts/objex/`)
- [x] Audit log — records bucket/object operations with user, action, timestamp, details; Admin-only UI at `/audit` with server-side pagination
- [x] PostgreSQL as opt-in database backend (`DATABASE_PROVIDER=postgresql`)
- [x] Separate PostgreSQL migration assembly (`ObjeX.Migrations.PostgreSql`)
- [x] Hangfire on PostgreSQL via `Hangfire.PostgreSql`
- [x] `docker-compose.postgres.yml` with Postgres service + health check
- [x] Image and PDF inline previews — eye button opens dialog; supports image, video, audio, PDF, text
- [x] Bulk select — checkbox column, bulk delete + ZIP download, action bar
- [x] File metadata viewer — info button opens dialog with key, content-type, size, ETag, uploaded, last modified
- [x] Storage analytics charts — per-bucket breakdown: storage donut, objects column, file types donut (top-N limits, custom tooltips)
- [x] ETag integrity verification on read — opt-in `x-objex-verify-integrity: true` request header; re-hashes blob before streaming, returns 500 on mismatch; zero overhead for normal clients
- [x] Automated test suite — 113 xUnit tests (unit + integration via `WebApplicationFactory`, real SQLite, no mocks); S3 CRUD, multipart, auth boundaries, path traversal, quotas, audit, copy, batch delete, resilience, cookie auth, health checks, security headers

---

## v2 — Search, Tags & Lifecycle

### Object Metadata & Tags
- Key-value tags per object (stored in DB)
- Tag-based search and filtering
- Tag management UI
- Lifecycle policies based on tags (e.g. auto-delete after X days)
- Retention policy enforcement

### Soft Delete
- Tombstone objects instead of hard-deleting, TTL-based purge
- Pairs naturally with lifecycle policies and tag-based retention
- Recoverable deletes via UI or API within retention window

### Advanced Search
- Full-text filename search
- Filter by content-type, size range, date range
- Tag-based filtering
- Faceted search UI
- Saved searches and search history

### Bucket Permissions (ACL)
- Per-bucket access control list (ACL)
- Read / Write / Delete permissions per user
- Permission management UI (admin)
- Permission checks enforced in all API endpoints

### Webhook Notifications
- POST to configured URL on bucket/object mutations (upload, delete, create bucket)
- Global webhook URL in Settings UI
- JSON payload with action, bucket, key, user, timestamp
- Alert on quota exceeded, integrity failures

### CLI Tool
- `objex` CLI — manage buckets, upload/download, admin ops
- Could wrap the S3 API or add a dedicated management API
- Complements the Blazor UI for scripting and automation

### Benchmark Suite
- Performance test suite — throughput, latency, concurrency
- Useful baseline for validating future optimizations
- Compare SQLite vs PostgreSQL, single vs multi-file workloads

### Extended Test Coverage
- PostgreSQL integration tests (currently SQLite-only)
- Large file streaming (500MB+) under memory pressure
- Fault injection: disk full mid-upload, `SQLITE_BUSY` contention
- S3 POST Object (presigned POST form-field auth)
- Hangfire job verification (orphan cleanup, integrity check)
- Backup/restore end-to-end drill

### ETag Verification on Read (v2)
- Streaming MD5 passthrough on all reads, log mismatches silently
- Expose corruption counters on `/health/integrity`

### S3 Conformance Badge
- Run Ceph s3-tests or mint test suite, publish pass rate
- Builds trust with adopters evaluating S3 compatibility

### Mobile-Responsive UI
- CSS/layout work on existing Blazor components

---

## v3 — Multi-Tenant, Enterprise & Hardening

### Teams & Organizations
- Multi-tenant support with organization workspaces
- Team membership (Owner / Admin / Member roles)
- Per-organization storage quotas
- Team invitation flow
- Org-scoped bucket and object isolation

### SSO / OIDC
- OAuth / SSO (Google, GitHub, OIDC)
- "Login with GitHub" for teams
- Auth pipeline integration

### Object Versioning
- Object version history, keep N prior versions, restore/rollback
- Fundamental model change — touches metadata schema, storage layout, every endpoint
- S3 compat: `?versionId=` on GET/HEAD/DELETE, version listing
- Pairs with soft delete for full data recovery story

### Encryption at Rest
- AES-256 per-object encryption, stream wrapper + key management
- Per-bucket or global encryption toggle
- S3 SSE-compatible headers (`x-amz-server-side-encryption`)

### Bucket Policies (JSON)
- S3-compatible policy engine
- Complex rule evaluation
- Policy management UI

### HTTPS / TLS
- TLS termination via Kestrel with certs, or reverse proxy (Caddy/Traefik) documentation
- Important for production deployments; presigned URLs are insecure over plain HTTP

### HTTP/2 + Keepalive
- Kestrel supports HTTP/2 natively, just needs config
- Low effort, good for concurrent S3 client performance

### OpenTelemetry Tracing
- Distributed tracing to complement existing Serilog + Prometheus
- Trace S3 requests end-to-end through middleware → service → storage

### Backpressure Handling
- Per-client throttling, upload queue limits
- Builds on existing `UseRateLimiter`; prevents resource exhaustion under load

### Built-in Backup Scheduling
- SQLite DB → remote S3 backup, configurable from UI
- Scheduled via Hangfire, unique differentiator for self-hosted storage

### File Comments / Annotations
- New model + UI for per-object comments
- Collaboration feature no S3-compatible store offers

### Storage Backends
- Implement additional `IObjectStorageService` backends (cloud, chunked, etc.)
- Swap in without changing any other layer

---

## v4 — Distribution & Scale

### Distribution / Sharding
- Partition objects across nodes by consistent hash
- The architectural leap from single-node to distributed storage
- Requires cluster membership, routing layer, metadata coordination

### Replication & Backup
- Sync blobs to secondary nodes or external targets
- Could start simple (rsync/rclone push) and evolve to real-time replication
- Cross-region or cross-site redundancy

### Erasure Coding
- Reed-Solomon encoding across shards for fault tolerance without full replication
- Higher storage efficiency than mirroring (e.g. 1.5x vs 3x overhead)
- Requires multi-node; advanced data path

### Node Rebalancing
- Redistribute objects when nodes join or leave the cluster
- Background migration with minimal impact on live traffic
- Consistent hashing ring updates

### Hot Object Caching / Cold Storage
- In-memory or Redis cache for frequently accessed blobs
- Tiered storage: hot (SSD/local) → warm (disk) → cold (archive/cloud)
- Access pattern tracking to drive tier placement

### FUSE Mount
- Expose buckets as a local filesystem via FUSE
- CSI driver for Kubernetes volume mounts
- Niche but powerful for legacy app integration

---

## Future Considerations

- Unicode key normalization — macOS clients upload with NFD normalization, Linux with NFC; the same filename produces different SHA256 hashes and is stored as two separate objects. Fix: normalize all keys to NFC on ingest
- Backup tooling — `export` / `restore` CLI commands; scheduled backup to local path or remote (S3, rclone)
- Metadata rebuild from disk — currently impossible; would require storing the logical key alongside the blob (e.g. in a sidecar file or blob header), then scanning blobs to reconstruct `objex.db` after total DB loss
- Hangfire on separate SQLite file — reduces lock contention between job store and EF Core under write-heavy load
- Migration CLI — `objex migrate --from s3://old-minio-bucket` for adoption from existing S3 providers
- Content-based search (Elasticsearch integration)
- Image recognition / auto-tagging (ML)
- CDN integration
- Storage usage over time charts (requires historical data collection)
- Rclone compatibility — S3 compat likely covers `rclone sync` out of the box; needs testing to confirm
