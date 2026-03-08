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
- Dashboard: total buckets, object count, storage used (10s auto-refresh)
- Bucket management: create (with inline validation), list, delete
- File browser: list objects, virtual key paths displayed
- Drag-and-drop / file-picker upload dialog with multi-file support
- Download (native `<a download>` → API) and delete per object
- Toast notifications bottom-right

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

## Phase 1 — Infrastructure

### 1. Dockerization
- Multi-stage Dockerfile (SDK build → runtime image)
- Volume mounts for `/data` (blobs + SQLite)
- Environment-based config (`ASPNETCORE_ENVIRONMENT`, `Storage__BasePath`, connection string)
- `docker-compose.yml` for local self-hosting
- Multi-arch builds: amd64 + arm64 (GitHub Actions)
- Docker Hub publishing

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
- Bulk select: download as zip, delete multiple
- Folder navigation (prefix-based virtual paths)
- Dark mode toggle
- Mobile-responsive layout
- File metadata viewer (content-type, ETag, size, dates)
- Storage analytics charts (usage over time, per-bucket breakdown)

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

## Phase 5 — Storage Extensibility

### 11. Storage Backends
- Implement additional `IObjectStorageService` backends (cloud, chunked, etc.)
- Swap in without changing any other layer

### 12. PostgreSQL Support
- Swap SQLite for PostgreSQL via same `IMetadataService` interface
- Configuration-driven backend selection

---

## Future Considerations

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
