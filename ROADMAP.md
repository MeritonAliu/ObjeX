# ObjeX Roadmap

---

## Completed

### Content-Addressable Blob Storage ✅
- `IHashService` interface in `ObjeX.Core` (BCL only, no NuGet)
- `Sha256HashService` in `ObjeX.Infrastructure/Hashing/` — SHA256 of `"{bucket}/{key}"`, 64-char lowercase hex
- `FileSystemStorageService` stores blobs at `{basePath}/{bucket}/{L1}/{L2}/{hash}.blob`
  - L1/L2 = first 4 hex chars → 65,536 directories, even distribution
  - Logical key (virtual path) lives in DB only — no filesystem folders for virtual paths
- `CleanupOrphanedBlobsAsync(IReadOnlySet<string>)` — caller-triggered GC, no scheduling
- `IHashService` registered as singleton in `Program.cs`

### Blazor UI — Basic ✅
- Radzen Blazor component library (replaced MudBlazor)
- Dashboard: total buckets, object count, storage used (10s auto-refresh)
- Bucket management: create (with validation), list, delete
- File browser: list objects, virtual key paths displayed
- Drag-and-drop / file-picker upload dialog with multi-file support
- Download (native `<a download>` → API) and delete per object
- Toast notifications bottom-right

---

## Phase 1 — Infrastructure & Core UX

### 1. Dockerization (~2 days)
- Multi-stage Dockerfile (SDK build → runtime image)
- Volume mounts for `/data` (blobs + SQLite)
- Environment-based config (`ASPNETCORE_ENVIRONMENT`, `Storage__BasePath`, connection string)
- `docker-compose.yml` for local self-hosting
- Multi-arch builds: amd64 + arm64 (GitHub Actions)
- Docker Hub publishing

### 3. API Key Authentication (~2 days)
- `X-API-Key` header middleware
- `ApiKey` model in database (key hash, name, expiry, created date)
- Key generation and management endpoints
- Blazor UI for creating/revoking keys
- Key expiry enforcement

---

## Phase 2 — S3 Compatibility & Large Files

### 4. S3 API Compatibility (~2–3 weeks)
- S3-style routes: `PUT /{bucket}/{key}`, `GET /{bucket}/{key}`, `DELETE /{bucket}/{key}`
- XML response formatting (S3 uses XML, not JSON)
- AWS Signature V4 authentication (HMAC-SHA256 canonical request)
- S3 error codes and error response format
- Compatibility testing with `aws-cli`, `boto3`, `s3cmd`
- `ListObjects` / `ListObjectsV2` with prefix + delimiter support

### 5. Multipart Upload (~1 week)
- `POST /{bucket}/{key}?uploads` — InitiateMultipartUpload
- `PUT /{bucket}/{key}?partNumber={n}&uploadId={id}` — UploadPart
- `POST /{bucket}/{key}?uploadId={id}` — CompleteMultipartUpload
- `DELETE /{bucket}/{key}?uploadId={id}` — AbortMultipartUpload
- Temporary part storage, part ETag tracking
- Atomic final object assembly
- 5 GB+ file support
- Garbage collection for abandoned uploads

### 6. Presigned URLs (~3 days)
- HMAC-SHA256 signed URLs with expiry
- Expiry enforcement on every request
- Support both download (GET) and upload (PUT) URLs
- Share link generation in Blazor UI

---

## Phase 3 — UI & Object Model Enhancements

### 7. Enhanced Blazor UI (~1 week)
- Image and PDF inline previews
- Bulk select: download as zip, delete multiple
- Folder navigation (prefix-based virtual paths)
- Dark mode toggle
- Mobile-responsive layout
- File metadata viewer (content-type, ETag, size, dates)
- Storage analytics charts (usage over time, per-bucket breakdown)

### 8. Object Metadata & Tags (~1 week)
- Key-value tags per object (stored in DB)
- Tag-based search and filtering
- Tag management UI
- Lifecycle policies based on tags (e.g. auto-delete after X days)
- Retention policy enforcement

### 9. Advanced Search (~1 week)
- Full-text filename search
- Filter by content-type, size range, date range
- Tag-based filtering
- Faceted search UI
- Saved searches
- Search history

---

## Phase 4 — Auth & Multi-User

### 10. User Authentication (~1 week)
- ASP.NET Core Identity (users, password hashing, sessions)
- Roles: Admin, User, ReadOnly
- Login/register Blazor pages
- JWT tokens for API access
- Session management and logout

### 11. Bucket Permissions (~2 weeks)
- Per-bucket access control list (ACL)
- Read / Write / Delete permissions per user
- Permission management UI (admin)
- Permission checks enforced in all endpoints

### 12. Teams & Organizations (~3 weeks)
- Multi-tenant support with organization workspaces
- Team membership (Owner / Admin / Member roles)
- Per-organization storage quotas
- Team invitation flow
- Org-scoped bucket and object isolation

---

## Phase 5 — Storage Extensibility

### 13. Storage Backends
- Implement additional `IObjectStorageService` backends (cloud, chunked, etc.)
- Swap in without changing any other layer

### 14. PostgreSQL Support
- Swap SQLite for PostgreSQL via same `IMetadataService` interface
- Configuration-driven backend selection

### 15. Advanced Search (~1 week)
- Full-text filename search
- Filter by content-type, size range, date range
- Tag-based filtering
- Faceted search UI
- Saved searches and search history

---

## Future Considerations

- Content-based search (Elasticsearch integration)
- Image recognition / auto-tagging (ML)
- Object versioning
- Lifecycle policies: auto-delete after X days
- Replication and redundancy
- Kubernetes Helm charts
- CDN integration
- Migration tools from S3 / MinIO
- Performance benchmarks
- Compliance: audit logs, encryption at rest
