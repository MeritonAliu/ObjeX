# Changelog

All notable changes to ObjeX are documented here.

---

## v1.1.0 — 2026-04-09

### Added
- NuGet lock files for reproducible builds
- Dockerfile HEALTHCHECK instruction
- Helm chart: Kubernetes Secret for sensitive values, securityContext, ServiceAccount, NOTES.txt, `_helpers.tpl`
- CI: NuGet cache, job timeout, concurrency control with cancel-in-progress
- CD: CI gate before publish, Docker layer cache, concurrency control
- docker-compose healthcheck for objex service

### Fixed
- Deactivated users could still access S3 API via SigV4 credentials
- Open redirect on login via unvalidated `returnUrl`
- XML injection in SigV4 error responses
- CORS policy applied globally instead of S3 port only
- Custom metadata headers writable to arbitrary response headers (now filtered to `x-amz-meta-*` on read)
- Insecure password generation using `Random.Shared` instead of `RandomNumberGenerator`
- CopyObject skipped destination key validation
- Batch delete accepted unlimited keys (now capped at 1000 per S3 spec)
- ListParts returned parts without ownership check
- Blazor object delete only removed metadata, left orphaned blob on disk
- HashingStream did not dispose inner stream (file handle leak)
- Fire-and-forget task using scoped DbContext in SigV4 middleware
- `eval()` calls in Settings and MainLayout replaced with proper JS interop
- Dashboard file type chart showing raw MIME subtypes instead of file extensions
- Objects by Bucket chart label overlap
- Upload dialog drag-and-drop not working (missing JS interop wiring)

### Improved
- ZIP download streams directly to response instead of buffering entire archive in memory
- Upload dialog passes file streams directly to storage instead of buffering into byte arrays
- Dashboard loads file type stats via server-side query instead of all objects into memory
- Users page N+1 role query eliminated with batch loading
- CancellationToken propagation in S3 upload, download, copy, and delete paths
- UpdateBucketStats uses direct update instead of load-then-save
- Dockerfile runs as non-root user, NuGet restore layer caching
- Radzen.Blazor pinned to exact version (was `*` wildcard)
- Microsoft packages aligned to 10.0.5
- Duplicate Identity package reference removed from Api project

### Removed
- Unused `User.StorageUsedBytes` property

---

## v1.0.0 — 2026-04-01

### Added
- PostgreSQL as opt-in database backend (`DATABASE_PROVIDER=postgresql`)
- Separate PostgreSQL migration assembly (`ObjeX.Migrations.PostgreSql`)
- Hangfire on PostgreSQL via `Hangfire.PostgreSql`
- `docker-compose.postgres.yml` with Postgres service + health check
- Audit log — records bucket/object operations with user, action, timestamp; Admin-only UI at `/audit`
- Prometheus `/metrics` endpoint with per-bucket storage gauges
- Helm chart for Kubernetes deployment
- Storage quota enforcement (per-user and global default)
- Image and PDF inline previews — eye button opens dialog; supports image, video, audio, PDF, text
- Bulk select — checkbox column, bulk delete + ZIP download, action bar
- File metadata viewer — info button opens dialog with key, content-type, size, ETag, uploaded, last modified
- Storage analytics charts — per-bucket breakdown: storage donut, objects column, file types donut
- ETag integrity verification on read — opt-in `x-objex-verify-integrity: true` header re-hashes blob before streaming
- Automated test suite — 113 xUnit tests (unit + integration), CI runs tests on every push and PR

### Fixed
- BucketNameValidator rejected bucket names starting/ending with digits
- Duplicate bucket creation returned 500 instead of S3-standard 409 `BucketAlreadyExists`
- SigV4 auth missing role claims — Admin/Manager privileges now enforced via S3 API
- Concurrent uploads to same key could fail with IOException due to `.tmp` file collision

## v0.2.0 — 2026-03-31

### Added
- S3 POST Object (presigned POST via form-field auth)
- S3 DeleteObjects batch delete (`POST /{bucket}?delete`)
- `x-amz-meta-*` custom metadata — stored as JSON on BlobObject, returned on GET/HEAD, captured on PUT/POST Object
- CopyObject — `PUT /{bucket}/{*key}` with `x-amz-copy-source` header; copies blob + metadata server-side
- ListMultipartUploads — `GET /{bucket}?uploads` lists active multipart uploads
- Stub 501 responses for unsupported bucket operations (versioning, lifecycle, policy, cors, encryption, tagging, acl)
- Startup seeding of buckets and S3 credentials via config/env vars
- User management UI — Admin/Manager roles, create/deactivate/delete users, forced password change
- Bucket ownership — users see only their own buckets; Admin/Manager see all
- ListObjectsV2 (`?list-type=2`) with continuation token support
- Presigned PUT URL support

### Fixed
- Object action icon visibility in light mode

## v0.1.0 — 2026-03-29

### Added
- Core blob storage with content-addressable filesystem layout
- S3-compatible API on port 9000 — bucket CRUD, object CRUD, AWS Signature V4 auth
- S3 multipart upload (Initiate/UploadPart/Complete/Abort) with 5GB+ support
- Presigned GET URLs with configurable expiry
- Blazor Server UI — dashboard with charts, bucket browser, virtual folder navigation, drag-and-drop upload, dark mode
- ASP.NET Core Identity with Admin/User roles, cookie auth for UI, SigV4 for S3
- S3 credential management (create/delete, one-time secret display)
- Hangfire background jobs — orphan blob cleanup, integrity verification, abandoned multipart cleanup
- Profile page with username, email, password management
- Docker multi-arch image (amd64/arm64) published to GHCR
- CI pipeline (build gate on push to main and PRs)
- Health checks (`/health`, `/health/ready`)
