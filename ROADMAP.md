# ObjeX Roadmap

---

## Completed

### Content-Addressable Blob Storage ✅
- `IHashService` interface in `ObjeX.Core` (BCL only, no NuGet)
- `Sha256HashService` in `ObjeX.Infrastructure/Hashing/` — SHA256 of `"{bucket}/{key}"`, 64-char lowercase hex
- `FileSystemStorageService` stores blobs at `{basePath}/{bucket}/{L1}/{L2}/{hash}.blob`
  - L1/L2 = first 4 hex chars → 65,536 directories, even distribution
  - Logical key (virtual path) lives in DB only — no filesystem folders for virtual paths
- `IHashService` registered as singleton in `Program.cs`

### Hangfire Background Jobs ✅
- `Hangfire.Core` + `Hangfire.AspNetCore` + `Hangfire.Storage.SQLite` (reuses `objex.db`)
- Job classes in `ObjeX.Infrastructure/Jobs/` — no job logic in API layer
- `CleanupOrphanedBlobsJob` — weekly Sunday 03:00 UTC, returns `CleanupResult` (checked/deleted/duration/timestamp) visible in Hangfire dashboard history
- Dashboard at `/hangfire` — Admin role required (localhost bypassed for dev)
- `IMetadataService.ListAllObjectsAsync()` added for cross-bucket object enumeration

### Blazor UI ✅
- Radzen Blazor component library
- **Dashboard** (`/`) — total buckets, object count, storage used (10s auto-refresh)
- **Buckets page** (`/buckets`) — list all buckets, create (with inline name validation), delete with confirmation
- **Bucket detail / file browser** (`/buckets/{name}`) — lists all objects in a bucket, breadcrumb nav back to `/buckets`, upload button (reuses dialog), download per object (native `<a download>` → API endpoint), delete per object with confirmation
- **Drag-and-drop upload dialog** — multi-file picker, streams files to `IObjectStorageService` directly from Blazor (no API round-trip)
- **Settings page** (`/settings`) — API key management (create with one-time key display, list, delete)
- Toast notifications bottom-right via Radzen `NotificationService`

### CI/CD ✅
- **CI** (`ci.yml`) — triggers on push to `main` and all PRs; runs on `ubuntu-latest`; restore → build Release; fails fast on compile errors; no tests yet
- **CD** (`cd.yml`) — triggers on push to `main` and manual dispatch; runs on self-hosted runner (labels: `objex`, `cd`, `dev`); builds Debug with `ASPNETCORE_ENVIRONMENT=Development`; stops running instance with `pkill`, deploys to `~/objex-live/` via `rsync --exclude='data/'` (data directories preserved across deploys), starts app in a `screen` session (`screen -dmS objex dotnet ObjeX.Api.dll`)

### Dockerize ✅
- Multi-stage Dockerfile (SDK build → ASP.NET runtime)
- Multi-arch via `--platform=$BUILDPLATFORM` + `$TARGETARCH`
- `docker-compose.yml` with named volume for `/data`
- Environment variables for connection string and blob path baked into image defaults

### Virtual Folder Navigation ✅
- `ListObjectsResult` record in `ObjeX.Core/Models/` — `Objects` + `CommonPrefixes`
- `IMetadataService.ListObjectsAsync` accepts optional `prefix` and `delimiter` — prefix filter pushed to DB (`LIKE`), delimiter grouping in C#
- API list endpoint accepts `?prefix=&delimiter=` query params; returns `{ objects, commonPrefixes }`
- `GET /api/objects/{bucket}/download?prefix=` — streams ZIP of all objects under a prefix
- Blazor `Objects.razor` — unified file+folder grid, breadcrumb, New Folder button, folder delete (recursive), folder ZIP download, upload into current prefix
- Placeholder objects (key ends with `/`, `ContentType: application/x-directory`) used for empty folders; filtered from file rows in UI

### Dark Mode ✅
- System preference detected via `prefers-color-scheme` on first visit (inline `<script>` in `<head>`)
- Cookie `objex-theme` persists choice across sessions
- `App.razor` reads cookie server-side via `IHttpContextAccessor` → passes to `<RadzenTheme>` — no flash on load
- `ThemeService` (Radzen) registered as `AddScoped<ThemeService>()` — drives client-side switching
- Toggle (`RadzenSwitch`) in Settings page; initial state read from cookie via JS in `OnAfterRenderAsync`

### Atomic Blob Writes ✅
- `FileSystemStorageService.StoreAsync` writes to `{hash}.blob.tmp` then `File.Move(..., overwrite: true)` — atomic on Linux
- On exception, `.tmp` file deleted in `catch` — final path never touched
- Startup cleanup: `.tmp` files older than 1 hour deleted automatically, count logged

### Authentication & Authorization ✅
- **ASP.NET Core Identity** — `User` extends `IdentityUser`, `ObjeXDbContext` extends `IdentityDbContext<User>`
- **Roles** — `Admin` and `User` seeded on first startup
- **Default admin** — `admin` / `admin` (configurable via `DefaultAdmin:*` config keys; warned in logs)
- **Login UI** — `/login` page with Radzen styling, username-or-email support, error toast, auth guard (redirects to `/` if already logged in)
- **Logout** — `GET /account/logout` clears cookie, redirects to `/login`
- **Global Blazor protection** — `AuthorizeView` in `MainLayout.razor` wraps all pages; `Login.razor` uses `EmptyLayout` to opt out
- **Cookie auth for browser** — default scheme is `Identity.Application`; challenges redirect to `/login`
- **API key auth for external clients** — `X-API-Key` header; `ApiKeyAuthenticationMiddleware` validates key, updates `LastUsedAt`, sets `context.User`
- **API keys UI** — Settings page at `/settings` lists keys, creates new (with one-time display of key value), deletes
- **API key endpoints** — `POST/GET/DELETE /api/keys`; key value shown only on creation
- **Proper 401 for API paths** — `ConfigureApplicationCookie` suppresses redirect for `/api/*`; `UseStatusCodePagesWithReExecute` skipped for `/api/*` to prevent cascade to login page
- **Hangfire dashboard** — `HangfireAuthorizationFilter` allows localhost unconditionally; requires `Admin` role otherwise

---

## Phase 1 — Infrastructure ✅ Complete

---

## Phase 2 — S3 Compatibility & Large Files

### 2. S3 API Compatibility
- S3-style routes: `PUT /{bucket}/{key}`, `GET /{bucket}/{key}`, `DELETE /{bucket}/{key}`
- XML response formatting (S3 uses XML, not JSON)
- AWS Signature V4 authentication (HMAC-SHA256 canonical request)
- S3 error codes and error response format
- Compatibility testing with `aws-cli`, `boto3`, `s3cmd`
- `ListObjects` / `ListObjectsV2` with prefix + delimiter support

### 3. Multipart Upload
- `POST /{bucket}/{key}?uploads` — InitiateMultipartUpload
- `PUT /{bucket}/{key}?partNumber={n}&uploadId={id}` — UploadPart
- `POST /{bucket}/{key}?uploadId={id}` — CompleteMultipartUpload
- `DELETE /{bucket}/{key}?uploadId={id}` — AbortMultipartUpload
- Temporary part storage, part ETag tracking
- Atomic final object assembly
- 5 GB+ file support
- Garbage collection for abandoned uploads

### 4. Presigned URLs
- HMAC-SHA256 signed URLs with expiry
- Expiry enforcement on every request
- Support both download (GET) and upload (PUT) URLs
- Share link generation in Blazor UI

---

## Phase 3 — UI & Object Model Enhancements

### 5. Enhanced Blazor UI
- Image and PDF inline previews
- Bulk select: delete multiple
- Mobile-responsive layout
- File metadata viewer (content-type, ETag, size, dates)
- Storage analytics charts (usage over time, per-bucket breakdown)
- ~~Folder navigation~~ ✅, ~~Dark mode~~ ✅, ~~ZIP download~~ ✅

### 6. Object Metadata & Tags
- Key-value tags per object (stored in DB)
- Tag-based search and filtering
- Tag management UI
- Lifecycle policies based on tags (e.g. auto-delete after X days)
- Retention policy enforcement

### 7. Advanced Search
- Full-text filename search
- Filter by content-type, size range, date range
- Tag-based filtering
- Faceted search UI
- Saved searches and search history

---

## Phase 4 — Multi-User & Permissions

### 8. User Management UI
- Identity backend already implemented (User model, roles, password hashing)
- Registration page
- Admin user list (view, deactivate, role assignment)
- Password reset flow (requires real email sender — currently `NoOpEmailSender`)
- Email verification

### 9. Bucket Permissions
- Per-bucket access control list (ACL)
- Read / Write / Delete permissions per user
- Permission management UI (admin)
- Permission checks enforced in all API endpoints

### 10. Teams & Organizations
- Multi-tenant support with organization workspaces
- Team membership (Owner / Admin / Member roles)
- Per-organization storage quotas
- Team invitation flow
- Org-scoped bucket and object isolation

---

## Phase 5 — Testing & Hardening

### 13. Automated Test Suite

CI is currently build-only. Before production-ready claim, need:

**Integration tests (real SQLite, no mocks):**
- Upload → download round-trip with ETag verification
- Upload → delete → confirm 404
- Bucket CRUD lifecycle
- Auth boundary: no key, expired key, wrong key, valid cookie, valid API key
- Path traversal fuzzing on object keys (`../`, `..\\`, URL-encoded variants)
- Concurrent uploads to same key (race condition validation)
- Large file streaming (500MB+)

**Fault injection:**
- Disk full mid-upload — confirm `.tmp` cleaned, 500 returned, no partial state
- Corrupt blob file — current: returns corrupt bytes with 200; target: detect via ETag mismatch on read
- Missing blob with valid metadata — confirm 404, not 500
- `SQLITE_BUSY` simulation under concurrent write load

**Operational:**
- Backup and restore drill — stop, backup, wipe, restore, verify `/health/ready` + spot-check downloads
- Hangfire GC job triggered manually — verify orphaned blobs removed, metadata-backed blobs untouched

### 14. ETag Verification on Read

Currently ETag is computed on upload and stored, but never checked on download. A corrupt or partially-overwritten blob file returns 200 with bad bytes. Fix: re-hash on read and compare; return 500 with a clear error if mismatch.

---

## Phase 7 — Storage Extensibility

### 15. Storage Backends
- Implement additional `IObjectStorageService` backends (cloud, chunked, etc.)
- Swap in without changing any other layer

### 16. PostgreSQL Support
- Swap SQLite for PostgreSQL via same `IMetadataService` interface
- Configuration-driven backend selection

---

## Future Considerations

- Unicode key normalization — macOS clients upload with NFD normalization, Linux with NFC; the same filename produces different SHA256 hashes and is stored as two separate objects. Fix: normalize all keys to NFC on ingest
- Prometheus `/metrics` endpoint — `prometheus-net.AspNetCore`; expose request rates, upload/download counts, storage used, active connections; enables Grafana dashboards
- Backup tooling — `export` / `restore` CLI commands; scheduled backup to local path or remote (S3, rclone)
- Metadata rebuild from disk — currently impossible; would require storing the logical key alongside the blob (e.g. in a sidecar file or blob header), then scanning blobs to reconstruct `objex.db` after total DB loss
- Hangfire on separate SQLite file — reduces lock contention between job store and EF Core under write-heavy load
- OAuth / SSO (Google, GitHub, OIDC)
- Content-based search (Elasticsearch integration)
- Image recognition / auto-tagging (ML)
- Object versioning
- Lifecycle policies: auto-delete after X days
- Replication and redundancy
- Kubernetes Helm charts
- CDN integration
- Migration tools from S3 / MinIO
- Compliance: audit logs, encryption at rest
