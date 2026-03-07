# ObjeX — AI Context

Self-hosted blob storage built with Clean Architecture in .NET 10.

---

## Project Layout

```
src/
├── ObjeX.Api/           # ASP.NET Core host — Program.cs, Endpoints/
├── ObjeX.Core/          # Domain — zero framework dependencies
│   ├── Interfaces/      # IMetadataService, IObjectStorageService, IHashService, IHasTimestamps
│   ├── Models/          # Bucket, BlobObject
│   └── Validation/      # BucketNameValidator
├── ObjeX.Infrastructure/
│   ├── Data/            # ObjeXDbContext (EF Core + SQLite)
│   ├── Hashing/         # Sha256HashService
│   ├── Metadata/        # SqliteMetadataService
│   ├── Migrations/      # EF Core migrations
│   └── Storage/         # FileSystemStorageService
└── ObjeX.Web/           # Blazor Server UI — components, pages, dialogs
```

---

## Architecture Rules

- **ObjeX.Core** has zero framework/NuGet dependencies — only BCL. Keep it that way.
- **ObjeX.Infrastructure** implements Core interfaces. Never reference Api or Web.
- **ObjeX.Api** wires everything together via DI in `Program.cs`. No business logic here.
- **ObjeX.Web** is for Blazor UI only. Components live here, hosted by ObjeX.Api.
- New storage backends → implement `IObjectStorageService`. New metadata stores → implement `IMetadataService`. No other changes needed.

---

## Core Interfaces

```csharp
// ObjeX.Core/Interfaces/IObjectStorageService.cs
// Responsibility: read/write actual file bytes to disk
public interface IObjectStorageService
{
    Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default);
    Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default);
    Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default);
}

// ObjeX.Core/Interfaces/IMetadataService.cs
// Responsibility: track object/bucket info in the database
public interface IMetadataService
{
    Task<Bucket> CreateBucketAsync(Bucket bucket, CancellationToken ctk = default);
    Task<Bucket?> GetBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<IEnumerable<Bucket>> ListBucketsAsync(CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<BlobObject> SaveObjectAsync(BlobObject blobObject, CancellationToken ctk = default);
    Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<IEnumerable<BlobObject>> ListObjectsAsync(string bucketName, CancellationToken ctk = default);
    Task DeleteObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default);
}

// ObjeX.Core/Interfaces/IHashService.cs
// Responsibility: compute deterministic hashes for storage path derivation
public interface IHashService
{
    string ComputeHash(string input); // returns 64-char lowercase hex string
}
```

---

## Models

```csharp
// Bucket: Id (Guid), Name, ObjectCount, TotalSize, Objects (nav), CreatedAt, UpdatedAt
// BlobObject: Id (Guid), BucketName, Key, Size, ContentType, ETag, StoragePath, Bucket (nav), CreatedAt, UpdatedAt
// Both implement IHasTimestamps
```

---

## Conventions

- **DB columns**: snake_case via `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`)
- **JSON responses**: camelCase, nulls omitted (`JsonNamingPolicy.CamelCase`, `WhenWritingNull`)
- **EF migrations**: run automatically on startup via `db.Database.Migrate()` in `Program.cs`
- **Bucket name rules**: 3–63 chars, lowercase alphanumeric + hyphens, no consecutive hyphens, no leading/trailing hyphens — enforced by `BucketNameValidator`
- **Object keys**: support slashes (virtual paths), sanitized by stripping `..` and normalising `\` → `/`
- **ETag**: MD5 of the uploaded stream, hex-encoded lowercase

---

## Storage Paths

- **Database**: `{solution-root}/objex.db` by default, or `ConnectionStrings:DefaultConnection` in config
- **Default blob path**: two levels up from `ContentRootPath` + `/data/blobs` (i.e. solution root)

The DB path logic in `Program.cs`:
```csharp
var solutionRoot = currentDir.Parent?.Parent?.FullName; // src/ObjeX.Api → src → solution root
var dbPath = Path.Combine(solutionRoot, "objex.db");
```

### Content-Addressable Blob Layout

Physical blob paths are **derived from a SHA256 hash of `"{bucketName}/{key}"`**, not from the key string itself. This is done by `IHashService` → `Sha256HashService`.

```
{basePath}/{bucketName}/{L1}/{L2}/{hash}.blob

L1 = hash[0..1]   (first 2 hex chars)
L2 = hash[2..3]   (next  2 hex chars)

Example:
  bucket = "photos", key = "2024/trip.jpg"
  hash   = sha256("photos/2024/trip.jpg") = "a3f7c2..."
  path   = /data/blobs/photos/a3/f7/a3f7c2....blob
```

**Why hashed paths:**
- Eliminates path traversal risk — the logical key never touches the filesystem raw
- Distributes files evenly across 256×256 = 65,536 directories — no hot directories
- Renaming or moving a logical key (future) doesn't require copying bytes, just updating `StoragePath` in the DB
- Decouples the public key namespace from the physical layout entirely

**Virtual folder paths** (e.g. `images/2024/photo.jpg`) live only in the database (`BlobObject.Key`). There are no corresponding subdirectories on disk for virtual folders.

**`CleanupOrphanedBlobsAsync(IReadOnlySet<string> knownStoragePaths)`** — call this on `FileSystemStorageService` to scan `*.blob` files and delete any not present in the known set. Use after bulk deletes or for periodic GC. No automatic scheduling — caller decides when to run it.

**Future:** content-based deduplication (hash file bytes, not key) is tracked in a TODO comment in `FileSystemStorageService`. Not implemented — would require ref-counting in metadata.

---

## Blazor UI Architecture

**Hosting model:** Blazor Server (InteractiveServer), not WASM.

**Combined host:** `ObjeX.Api` is the single process — it serves both the REST API and the Blazor UI. `ObjeX.Web` is a class library of components, referenced by `ObjeX.Api` as a project dependency. `ObjeX.Web/Program.cs` is dead scaffolding — ignore it.

**Data access from Blazor:** Components inject Core interfaces (`IMetadataService`) directly — no HttpClient, no API calls. Blazor runs server-side in the same process and DI container as the API, so direct injection is correct and efficient. The REST API is the public S3-compatible surface for external clients only.

```
Browser → SignalR → Blazor Server (ObjeX.Api process)
                         ↓
                   IMetadataService (Core interface)
                         ↓
                   SqliteMetadataService (Infrastructure)

External S3 clients → HTTP → ObjeX.Api endpoints → same services
```

**Render mode:** Set globally on `<Routes @rendermode="InteractiveServer" />` in `App.razor`. Do NOT add `@rendermode` per-page — the global setting covers all pages.

**UI library:** Radzen Blazor. Registered via `builder.Services.AddRadzenComponents()` in `Program.cs`. Required host components in `MainLayout.razor`: `<RadzenDialog />` and `<RadzenNotification />`.

**Validation pattern:**
- **Enforcement** → service layer only (`SqliteMetadataService` calls `BucketNameValidator`, throws `ArgumentException` on invalid input)
- **UX feedback** → Blazor dialogs use the same `BucketNameValidator` from Core for inline errors as the user types
- **API endpoints** → do NOT duplicate validation; catch `ArgumentException` from the service and return `400 BadRequest`
- Never call `BucketNameValidator` in both the API endpoint and the service — service is the single enforcer

**Input reactivity:** Use native `<input @oninput="...">` with `class="rz-textbox"` instead of `<RadzenTextBox>` when you need per-keystroke updates. Radzen's `ValueChanged` fires on `onchange` (blur), not `oninput`.

**EF Core + `init` properties:** Both `Bucket` and `BlobObject` use `Guid Id { get; init; } = Guid.NewGuid()`. EF Core 10 must be told not to generate its own value — both entities have `.ValueGeneratedNever()` configured in `ObjeXDbContext`. Do not remove this — removing it causes "Unexpected entry.EntityState: Detached" on insert.

**Dialogs:** Use `DialogService.OpenAsync<TComponent>("Title")` — returns the value passed to `DialogService.Close(value)`, or `null` if cancelled. Always null-check the return before acting on it.

**File downloads are the exception to "no API calls from Blazor":** Blazor Server runs on the server and cannot push file bytes to the browser's download manager through SignalR. The browser must make a direct HTTP GET request to download a file. Therefore, download buttons use a plain `<a href="/api/objects/..." download>` pointing at the API endpoint. This is not an architecture violation — it's a browser constraint. Rule of thumb: Blazor reads/writes data through the service layer; file downloads are a browser concern.

---

## Endpoint Routes

```
GET    /api/buckets               → list buckets
POST   /api/buckets?name={name}   → create bucket
GET    /api/buckets/{name}        → get bucket
DELETE /api/buckets/{name}        → delete bucket

PUT    /{bucket}/{*key}           → upload object
GET    /{bucket}/{*key}           → download object
DELETE /{bucket}/{*key}           → delete object
GET    /{bucket}/                 → list objects in bucket
```

---

## Key NuGet Packages

| Package                            | Used for                         |
|------------------------------------|----------------------------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite persistence           |
| `EFCore.NamingConventions`         | snake_case DB columns            |
| `Scalar.AspNetCore`                | Interactive API docs at `/scalar/v1` |
| `Serilog.AspNetCore`               | Structured request logging       |
| `Microsoft.AspNetCore.OpenApi`     | OpenAPI spec generation          |

---

## Run Locally

```bash
cd src/ObjeX.Api
dotnet run
# → http://localhost:8080
# → http://localhost:8080/scalar/v1  (API docs)
# → http://localhost:8080/health
```

## EF Migrations

```bash
cd src/ObjeX.Api
dotnet ef migrations add <MigrationName> --project ../ObjeX.Infrastructure
dotnet ef database update  # or just run the app — auto-migrates
```

---

## Roadmap

See ROADMAP.md for the full plan. Priority order:

1. **Dockerize** — multi-stage Dockerfile, docker-compose, volume mounts for `/data`, multi-arch
2. **Blazor UI (basic)** — Radzen, dashboard stats, bucket CRUD, file browser, drag-drop upload ✅ in progress
3. **API Key Auth** — `X-API-Key` middleware, `ApiKey` model in DB, expiry, key management UI
4. **Object listing with prefix/delimiter** — prefix + delimiter params in `ListObjectsAsync`
5. **S3 Compatibility** — `/{bucket}/{key}` routes, XML responses, AWS Sig V4, S3 error codes
6. **Multipart Upload** — Initiate/UploadPart/Complete endpoints, temp part storage, 5GB+ support
7. **Presigned URLs** — HMAC-SHA256 signed URLs, expiry enforcement, upload + download
8. **Enhanced Blazor UI** — previews, bulk ops, folder nav, dark mode, analytics charts
9. **Object Tags** — key-value tags, tag-based search, lifecycle/retention policies
10. **Advanced Search** — full-text filename, filter by type/size/date, faceted search
11. **User Auth** — ASP.NET Core Identity, roles (Admin/User/ReadOnly), JWT for API
12. **Bucket Permissions** — per-bucket ACL, read/write/delete, permission checks in endpoints
13. **Teams/Orgs** — multi-tenant, org workspaces, team roles, storage quotas
14. **Storage backends** — swap `FileSystemStorageService` for cloud or chunked storage
15. **PostgreSQL support** — swap SQLite via same `IMetadataService` interface
