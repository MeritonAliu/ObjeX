# ObjeX — AI Context

Self-hosted blob storage built with Clean Architecture in .NET 10.

---

## Project Layout

```
src/
├── ObjeX.Api/           # ASP.NET Core host — Program.cs, Endpoints/
├── ObjeX.Core/          # Domain — zero framework dependencies
│   ├── Interfaces/      # IMetadataService, IObjectStorageService, IHasTimestamps
│   ├── Models/          # Bucket, BlobObject
│   └── Validation/      # BucketNameValidator
├── ObjeX.Infrastructure/
│   ├── Data/            # ObjeXDbContext (EF Core + SQLite)
│   ├── Metadata/        # SqliteMetadataService
│   ├── Migrations/      # EF Core migrations
│   └── Storage/         # FileSystemStorageService
└── ObjeX.Web/           # Blazor Server UI (scaffolded, pages not yet built)
```

---

## Architecture Rules

- **ObjeX.Core** has zero framework/NuGet dependencies — only BCL. Keep it that way.
- **ObjeX.Infrastructure** implements Core interfaces. Never reference Api or Web.
- **ObjeX.Api** wires everything together via DI in `Program.cs`. No business logic here.
- **ObjeX.Web** is for Blazor UI only. Currently scaffolded but empty.
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

- **Blobs**: `{Storage:BasePath}/{bucketName}/{sanitized-key}` — directories created on demand
- **Database**: `{solution-root}/objex.db` by default, or `ConnectionStrings:DefaultConnection` in config
- **Default blob path**: two levels up from `ContentRootPath` + `/data/blobs` (i.e. solution root)

The DB path logic in `Program.cs`:
```csharp
var solutionRoot = currentDir.Parent?.Parent?.FullName; // src/ObjeX.Api → src → solution root
var dbPath = Path.Combine(solutionRoot, "objex.db");
```

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
2. **Blazor UI (basic)** — MudBlazor, dashboard stats, bucket CRUD, file browser, drag-drop upload
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
