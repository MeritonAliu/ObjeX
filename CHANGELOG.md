# Changelog

All notable changes to ObjeX are documented here.

---

## Unreleased

- PostgreSQL as opt-in database backend (`DATABASE_PROVIDER=postgresql`)
- Audit log — records bucket/object operations with user, action, timestamp; Admin-only UI at `/audit`
- Prometheus `/metrics` endpoint with per-bucket storage gauges
- Helm chart for Kubernetes deployment
- Storage quota enforcement (per-user and global default)
- Automated test suite — 111 xUnit tests (unit + integration), CI runs tests on every push and PR

### Fixed
- BucketNameValidator rejected bucket names starting/ending with digits (`char.IsLower` → `char.IsLower || char.IsAsciiDigit`)
- Duplicate bucket creation returned 500 instead of S3-standard 409 `BucketAlreadyExists`
- SigV4 auth missing role claims — Admin/Manager privileges now enforced via S3 API (not just Blazor)
- Concurrent uploads to same key could fail with IOException due to `.tmp` file collision (now uses unique temp filenames)

## v0.2.0 — 2026-03-31

### Added
- S3 POST Object (presigned POST via form-field auth)
- S3 DeleteObjects batch delete (`POST /{bucket}?delete`)
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
