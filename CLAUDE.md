# ObjeX — AI Context

Self-hosted blob storage built with Clean Architecture in .NET 10.

---

## Project Layout

```
src/
├── ObjeX.Api/           # ASP.NET Core host — Program.cs, Endpoints/, Middleware/, Auth/
├── ObjeX.Core/          # Domain — zero framework dependencies
│   ├── Interfaces/      # IMetadataService, IObjectStorageService, IHashService, IHasTimestamps
│   ├── Models/          # Bucket, BlobObject, S3Credential, User, ListObjectsResult
│   ├── Utilities/       # HashingStream (MD5 passthrough for ETag computation during upload)
│   └── Validation/      # BucketNameValidator (GetValidationError)
├── ObjeX.Infrastructure/
│   ├── Data/            # ObjeXDbContext (EF Core + SQLite, extends IdentityDbContext<User>)
│   ├── Hashing/         # Sha256HashService
│   ├── Jobs/            # CleanupOrphanedBlobsJob, VerifyBlobIntegrityJob (Hangfire job classes)
│   ├── Metadata/        # SqliteMetadataService
│   ├── Migrations/      # EF Core migrations
│   └── Storage/         # FileSystemStorageService
└── ObjeX.Web/           # Blazor Server UI — components, pages, dialogs
    └── Components/
        ├── Pages/       # Dashboard, Buckets, Objects, Settings, Login, NotFound
        ├── Dialogs/     # CreateBucketDialog, UploadObjectDialog, CreateS3CredentialDialog, ShowS3CredentialDialog, CreateFolderDialog
        └── Layout/      # MainLayout, NavMenu, EmptyLayout
```

---

## Architecture Rules

- **ObjeX.Core** has zero framework/NuGet dependencies — only BCL. Keep it that way.
- **ObjeX.Infrastructure** implements Core interfaces. Never reference Api or Web.
- **ObjeX.Api** wires everything together via DI in `Program.cs`. No business logic here.
- **ObjeX.Web** references both `ObjeX.Core` and `ObjeX.Infrastructure` (for `ObjeXDbContext` injection in Blazor components).
- New storage backends → implement `IObjectStorageService`. New metadata stores → implement `IMetadataService`. No other changes needed.

---

## Authentication & Authorization

### Overview — Dual Auth

ObjeX uses **two authentication mechanisms** operating independently:

| Mechanism | Scheme name | Used by |
|---|---|---|
| Cookie (ASP.NET Core Identity) | `Identity.Application` | Browser / Blazor UI |
| AWS Signature V4 | `"SigV4"` (custom middleware) | S3 clients on port 9000 |

The cookie is the default for the browser. S3 clients on port 9000 authenticate via AWS Sig V4 — no cookie, no `X-API-Key`.

### Middleware Pipeline Order

```
UseStaticFiles
UseCors
UseRateLimiter
app.Use(...)               ← security headers (X-Content-Type-Options, X-Frame-Options, etc.)
UseAuthentication          ← runs Identity cookie handler, sets context.User for cookie sessions
UseWhen(port == 9000)      ← SigV4AuthMiddleware: validates Sig V4, sets context.User for S3 clients
UseAuthorization           ← enforces policies on the already-resolved context.User
```

### HTTP Security Headers

Set in a raw `app.Use` middleware in `Program.cs` (after `UseCors`, before auth):

| Header | Value | Condition |
|--------|-------|-----------|
| `Server` | removed | `AddServerHeader = false` on Kestrel |
| `X-Powered-By` | removed | `ctx.Response.Headers.Remove(...)` |
| `X-Content-Type-Options` | `nosniff` | always |
| `X-Frame-Options` | `SAMEORIGIN` | always |
| `X-Permitted-Cross-Domain-Policies` | `none` | always |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | always |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` | non-dev only |

CSP is intentionally omitted — Blazor Server requires inline scripts and a SignalR WebSocket (`ws://`/`wss://`), making a safe policy non-trivial. Deferred.

`SigV4AuthMiddleware` (`ObjeX.Api/Middleware/`) runs only on port 9000 via `app.UseWhen(ctx => ctx.Connection.LocalPort == 9000, ...)`. It: parses the `Authorization: AWS4-HMAC-SHA256 ...` header (or presigned query params), looks up the `AccessKeyId` in `db.S3Credentials`, validates the HMAC-SHA256 signature, checks timestamp freshness (±15 min, presigned URLs use `X-Amz-Expires`), verifies the payload hash against `x-amz-content-sha256`, then sets `context.User` to a `ClaimsIdentity` with scheme `"SigV4"`. Returns S3 XML error responses on failure.

### 401 vs 302 for API Paths

By default, cookie auth challenges redirect to the login page (302). For API endpoints this is wrong — external clients expect 401. Two fixes are applied:

1. **`ConfigureApplicationCookie`** in `Program.cs` overrides `OnRedirectToLogin` and `OnRedirectToAccessDenied`: if `Request.Path.StartsWithSegments("/api")`, sets `StatusCode = 401` and returns without redirecting.

2. **`UseStatusCodePagesWithReExecute`** is wrapped in `app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), ...)` so it only intercepts non-API responses. Without this, the 401 would be caught by the status code middleware and re-executed to `/not-found`, which then redirects to login.

### Authorization Policy

```csharp
// ApiPolicy — used by all API endpoints
options.AddPolicy("ApiPolicy", policy => policy.RequireAuthenticatedUser());
```

No `AddAuthenticationSchemes` on the policy — both auth mechanisms set `context.User` before `UseAuthorization` runs, so the policy just checks `IsAuthenticated`. Adding scheme names to the policy would cause a 500 because `"ApiKey"` has no registered ASP.NET auth handler (it's handled by our custom middleware, not the auth pipeline).

### ASP.NET Core Identity Setup

- `User` model in `ObjeX.Core/Models/` extends `IdentityUser`
- `ObjeXDbContext` extends `IdentityDbContext<User>`
- Roles: `Admin`, `User` — seeded on startup alongside default admin
- Password requirements relaxed for MVP (min 4 chars, no complexity rules)
- Email flows are no-ops — no `IEmailSender` registered, no email verification

**Default admin** (seeded on first run if no `admin` user exists):
```
Username: admin  (or DefaultAdmin:Username in config)
Email:    admin@objex.local  (or DefaultAdmin:Email)
Password: admin  (or DefaultAdmin:Password)
```
⚠️ Change this in production via `appsettings.json` or environment variables.

### Login / Logout

Blazor Server cannot set HTTP cookies — the SignalR response is already committed by the time component code runs. Auth actions that touch cookies are therefore handled by **real HTTP endpoints**, not Blazor components:

```
POST /account/login   ← HTML form POST; sets Identity cookie; redirects to returnUrl or /
GET  /account/logout  ← clears Identity cookie; redirects to /login
```

The login endpoint accepts `login` (username or email — detected by `@` presence), `password`, and `returnUrl` form fields. On failure it redirects back to `/login?error=1&login={value}` so the form can pre-fill the username.

`Login.razor` uses `@layout EmptyLayout` and `[AllowAnonymous]`. It renders a plain HTML `<form method="post" action="/account/login">` — not a Blazor event handler. It shows a Radzen toast notification on error (detected via `?error=1` query param in `OnAfterRenderAsync`).

### Blazor Global Route Protection

All pages using `MainLayout` are protected via `<AuthorizeView>` in `MainLayout.razor`. The `<Authorized>` branch renders the layout; `<NotAuthorized>` renders `<RedirectToLogin />`. `AuthorizeRouteView` alone is insufficient without per-page `[Authorize]` — `AuthorizeView` in the layout is the actual gate.

`RedirectToLogin.razor` calls `Navigation.NavigateTo("/login?returnUrl=...", forceLoad: true)` — `forceLoad: true` is required to escape the SignalR context and do a real page load.

`AddCascadingAuthenticationState()` is registered in DI (`Program.cs`). Do not use the `<CascadingAuthenticationState>` wrapper component — it cannot cascade to interactive children from a static SSR parent.

### S3Credential Model (`ObjeX.Core/Models/S3Credential.cs`)

```csharp
public class S3Credential : IHasTimestamps {
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string AccessKeyId { get; set; }      // "OBX" + 17 random uppercase alphanumeric (20 chars total)
    public required string SecretAccessKey { get; set; }  // 40 random bytes → base64url (~54 chars); stored plain — required for HMAC
    public required string UserId { get; set; }
    public User? User { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Credential generation:** use `S3Credential.Create(name, userId)` — returns `(S3Credential entity, string secretAccessKey)`. The secret is returned to the caller once (shown in UI) and stored in the DB plain — **this is intentional**: HMAC-SHA256 signing requires the original secret; a hashed version cannot be used for verification.

**Why plain storage:** AWS itself stores secret access keys in plain (or symmetrically encrypted) form. The security model is: protect the DB (encryption at rest, access control), not hash the secret. A hashed secret is incompatible with Sig V4.

EF Core `.ValueGeneratedNever()` on `Id`. Unique index on `AccessKeyId`.

### Hangfire Dashboard Auth

`HangfireAuthorizationFilter` (`ObjeX.Api/Auth/`) allows localhost unconditionally; otherwise requires `IsInRole("Admin")`. Dashboard is at `/hangfire`.

---

## Background Jobs (Hangfire)

Hangfire is wired in `ObjeX.Api` only. Job classes live in `ObjeX.Infrastructure/Jobs/` — no job logic in the API layer.

**Packages (ObjeX.Api only):** `Hangfire.Core`, `Hangfire.AspNetCore`, `Hangfire.Storage.SQLite`

**Storage:** Hangfire reuses the same `objex.db` SQLite file. Note: `Hangfire.Storage.SQLite` takes a **file path** (`/path/objex.db`), not an EF Core connection string (`Data Source=...`). The path is extracted from `connectionString` before passing to `UseSQLiteStorage(dbFilePath)`.

**DI registration:** `FileSystemStorageService` is registered as a singleton under its **concrete type first**, then aliased as `IObjectStorageService`. This lets the job inject the concrete type directly (no cast) while the rest of the app uses the interface:
```csharp
builder.Services.AddSingleton<FileSystemStorageService>(...);
builder.Services.AddSingleton<IObjectStorageService>(sp => sp.GetRequiredService<FileSystemStorageService>());
```

**Jobs:**

| Job class | Location | Schedule | Return type | What it does |
|---|---|---|---|---|
| `CleanupOrphanedBlobsJob` | `Infrastructure/Jobs/` | Weekly Sun 03:00 UTC | `Task<CleanupResult>` | Queries all known `StoragePath` values from metadata, scans `*.blob` files on disk, deletes any not in the known set |
| `VerifyBlobIntegrityJob` | `Infrastructure/Jobs/` | Weekly Sun 04:00 UTC | `Task<IntegrityResult>` | Reads every blob file, recomputes MD5, compares against stored ETag — logs errors for corrupted or missing blobs |

`CleanupResult` (record, defined in same file): `FilesChecked`, `FilesDeleted`, `DurationSeconds`, `Timestamp`.
`IntegrityResult` (record, defined in same file): `Checked`, `Corrupted`, `Missing`, `DurationSeconds`, `Timestamp`. Returning a value from the job method makes the result visible in the Hangfire dashboard job history.

`FileSystemStorageService.BasePath` is `internal` — accessible to jobs in the same `ObjeX.Infrastructure` assembly, not visible outside.

---

## Core Interfaces

```csharp
// ObjeX.Core/Interfaces/IObjectStorageService.cs
public interface IObjectStorageService
{
    Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default);
    Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default);
    Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default);
}

// ObjeX.Core/Interfaces/IMetadataService.cs
public interface IMetadataService
{
    Task<Bucket> CreateBucketAsync(Bucket bucket, CancellationToken ctk = default);
    Task<Bucket?> GetBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<IEnumerable<Bucket>> ListBucketsAsync(CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<BlobObject> SaveObjectAsync(BlobObject blobObject, CancellationToken ctk = default);
    Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, CancellationToken ctk = default);
    Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default); // all objects across all buckets — NOT filtered, used by Hangfire cleanup
    Task DeleteObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default);
}

// ObjeX.Core/Models/ListObjectsResult.cs
public record ListObjectsResult(IEnumerable<BlobObject> Objects, IEnumerable<string> CommonPrefixes);
// Objects = files at current level; CommonPrefixes = virtual folder paths (e.g. "photos/2024/")
// Placeholder objects (key ends with "/", ContentType "application/x-directory") are filtered from UI

// ObjeX.Core/Interfaces/IHashService.cs
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
// S3Credential: Id (Guid), Name, AccessKeyId, SecretAccessKey (plain), UserId, User (nav), LastUsedAt, CreatedAt, UpdatedAt
// User: extends IdentityUser — adds StorageUsedBytes
// All except User implement IHasTimestamps
```

---

## Conventions

- **DB columns**: snake_case via `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`)
- **JSON responses**: camelCase, nulls omitted (`JsonNamingPolicy.CamelCase`, `WhenWritingNull`)
- **EF migrations**: run automatically on startup via `db.Database.Migrate()` in `Program.cs`
- **Bucket name rules**: 3–63 chars, lowercase alphanumeric + hyphens, no consecutive hyphens, no leading/trailing hyphens — enforced by `BucketNameValidator`
- **Object keys**: support slashes (virtual paths). Validated by `ObjectKeyValidator.GetValidationError` (in `ObjeX.Core/Validation/`) — rejects empty, >1024 chars, leading `/`, control characters (including null bytes), and keys that normalize to empty after stripping `..` and `\`. `SanitizeKey` in `FileSystemStorageService` then strips `..` and normalises `\` → `/` before hashing — the logical key is stored as-is in DB, the physical path is always a SHA256 hash
- **ETag**: MD5 of the uploaded stream, hex-encoded lowercase

---

## Storage Paths

`Program.cs` contains fallback path logic (walks up to solution root) but it is overridden by `appsettings.json` in practice:

- **Database**: `./data/db/objex.db` (relative to working directory, set in `ConnectionStrings:DefaultConnection`)
- **Blob storage**: `./data/blobs` (relative to working directory, set in `Storage:BasePath`)

The fallback logic in `Program.cs` only activates when `ConnectionStrings:DefaultConnection` is absent from config — don't rely on it. Always configure explicit paths in appsettings for deployed instances.

### Content-Addressable Blob Layout

Physical blob paths are **derived from a SHA256 hash of `"{bucketName}/{key}"`**, not from the key string itself.

```
{basePath}/{bucketName}/{L1}/{L2}/{hash}.blob

L1 = hash[0..1]   (first 2 hex chars)
L2 = hash[2..3]   (next  2 hex chars)

Example:
  bucket = "photos", key = "2024/trip.jpg"
  hash   = sha256("photos/2024/trip.jpg") = "a3f7c2..."
  path   = /data/blobs/photos/a3/f7/a3f7c2....blob
```

**Atomic writes:** `StoreAsync` writes to `{hash}.blob.tmp` first, then `File.Move(..., overwrite: true)` into the final path. Move is atomic on Linux. On crash, the `.tmp` file is cleaned up at next startup (files older than 1 hour are deleted). This prevents corrupt blobs with valid metadata pointing to them.

**Why hashed paths:**
- Eliminates path traversal risk — the logical key never touches the filesystem raw
- Distributes files evenly across 256×256 = 65,536 directories — no hot directories
- Decouples the public key namespace from the physical layout entirely

---

## Blazor UI Architecture

**Hosting model:** Blazor Server (InteractiveServer), not WASM.

**Combined host:** `ObjeX.Api` is the single process — it serves both the REST API and the Blazor UI. `ObjeX.Web` is a class library of components, referenced by `ObjeX.Api` as a project dependency. `ObjeX.Web/Program.cs` is dead scaffolding — ignore it.

**Data access from Blazor:** Components inject Core interfaces (`IMetadataService`) or `ObjeXDbContext` directly — no HttpClient, no API calls. Blazor runs server-side in the same process and DI container as the API, so direct injection is correct and efficient.

```
Browser → SignalR → Blazor Server (ObjeX.Api process)
                         ↓
                   IMetadataService / ObjeXDbContext
                         ↓
                   SqliteMetadataService / EF Core

External S3 clients → HTTP → ObjeX.Api endpoints → same services
```

**Render mode:** Set globally on `<Routes @rendermode="InteractiveServer" />` in `App.razor`. Do NOT add `@rendermode` per-page — the global setting covers all pages.

**UI library:** Radzen Blazor. Registered via `builder.Services.AddRadzenComponents()` in `Program.cs`. Required host components in `MainLayout.razor`: `<RadzenDialog />` and `<RadzenNotification />`.

**Validation pattern:**
- **Enforcement** → service layer only (`SqliteMetadataService` calls `BucketNameValidator`, throws `ArgumentException` on invalid input)
- **UX feedback** → Blazor dialogs use the same `BucketNameValidator` from Core for inline errors as the user types
- **API endpoints** → do NOT duplicate validation; catch `ArgumentException` from the service and return `400 BadRequest`

**Input reactivity:** Use native `<input @oninput="...">` with `class="rz-textbox"` instead of `<RadzenTextBox>` when you need per-keystroke updates. Radzen's `ValueChanged` fires on `onchange` (blur), not `oninput`.

**EF Core + `init` properties:** Both `Bucket` and `BlobObject` use `Guid Id { get; init; } = Guid.NewGuid()`. EF Core 10 must be told not to generate its own value — both entities have `.ValueGeneratedNever()` configured in `ObjeXDbContext`. Do not remove this — removing it causes "Unexpected entry.EntityState: Detached" on insert. Same applies to `S3Credential.Id`.

**Dialogs:** Use `DialogService.OpenAsync<TComponent>("Title")` — returns the value passed to `DialogService.Close(value)`, or `null` if cancelled. Always null-check the return before acting on it. For complex return types, define a nested `public record` inside the dialog's `@code` block and reference it as `DialogComponent.RecordType` from the caller. Use `OpenAsync` (not `Alert`) when the dialog body needs rendered HTML — `Alert` renders plain text only.

Keyboard handling: text-input dialogs (`CreateBucketDialog`, `CreateS3CredentialDialog`) bind `@onkeydown` on the `<input>` — Enter submits (if valid), Escape cancels. `ShowS3CredentialDialog` binds `@onkeydown` on the container `<RadzenStack tabindex="-1">`. `CreateFolderDialog` follows the same pattern. Do NOT rely on Radzen's built-in Enter-to-submit — it doesn't exist.

`ShowS3CredentialDialog` displays both `AccessKeyId` and `SecretAccessKey` with copy-to-clipboard buttons. Shows a warning alert: "Save your secret access key now — it won't be shown again." The dialog has `CloseDialogOnOverlayClick = false, ShowClose = false` — user must click Done.

**File downloads are the exception to "no API calls from Blazor":** Blazor Server runs on the server and cannot push file bytes to the browser's download manager through SignalR. Download buttons use a plain `<a href="/api/objects/..." download>` pointing at the API endpoint. This is not an architecture violation — it's a browser constraint.

**Clickable links in grids:** Use `<a href="..." style="color:var(--rz-primary);text-decoration:none">` — do NOT use `<RadzenLink>` which renders red in the Material theme. This applies to bucket name links in `Buckets.razor` and `Dashboard.razor`.

**Virtual folder navigation:** `Objects.razor` tracks `_currentPrefix` (e.g. `"photos/2024/"`) as component state. Calls `ListObjectsAsync` with `delimiter: "/"` — folders render as clickable rows, files as regular rows in a unified `RadzenDataGrid`. Breadcrumb segments are `<span @onclick>` (not `RadzenLink`) to avoid full-page navigation. Folder create writes a zero-byte placeholder object with key `prefix/` and `ContentType: application/x-directory`. Upload prepends `_currentPrefix` to the file name. Placeholder objects (key ends with `/`) are filtered from file rows.

**Dark mode:** Theme stored in `objex-theme` cookie. `App.razor` reads cookie via `IHttpContextAccessor` server-side and passes to `<RadzenTheme>` — no flash on load. An inline `<script>` in `<head>` sets the cookie from `prefers-color-scheme` on first visit. Toggle in Settings page uses `ThemeService.SetTheme()` + JS cookie write. `ThemeService` is registered as `AddScoped<ThemeService>()` — do NOT use `AddRadzenCookieThemeService` (it fights the server-side rendering). Read initial switch state from cookie via JS in `OnAfterRenderAsync`, not from `ThemeService.Theme` (which is null on Blazor init).

**Font:** Inter, self-hosted in `wwwroot/fonts/` (weights 300–700). Applied globally via `:root { --rz-body-font-family: 'Inter' }` + `*:not(.material-icons):not(.material-icons-outlined):not([class*="rz-icon"]):not(i)` — the `:not()` exclusions are critical to prevent Material Icons from rendering as text.

**Theme colors:** Teal primary via CSS variable overrides in `app.css` loaded after `<RadzenTheme>` in `App.razor` — load order matters, loading before causes Radzen to overwrite the overrides. Overrides only `--rz-primary*` variables; do NOT override base background/text colors as they break light mode.

**Profile page** (`/profile`): username (alphanumeric only, no spaces, validated per-keystroke via `@oninput`), email, and password change sections. Uses `visibility: hidden` (not `@if`) for error messages to prevent layout shift. After username save: `forceLoad: true` reload to refresh NavMenu. After password change: forced logout (`Navigation.NavigateTo("/account/logout", forceLoad: true)`).

---

## Endpoint Routes

```
# Buckets (require ApiPolicy) — port 9001
GET    /api/buckets               → list buckets
POST   /api/buckets?name={name}   → create bucket
GET    /api/buckets/{name}        → get bucket
DELETE /api/buckets/{name}        → delete bucket

# Objects (require ApiPolicy) — port 9001
PUT    /api/objects/{bucket}/{*key}          → upload object
GET    /api/objects/{bucket}/{*key}          → download object
DELETE /api/objects/{bucket}/{*key}          → delete object
GET    /api/objects/{bucket}/                → list objects; accepts ?prefix=&delimiter= query params; returns { objects, commonPrefixes }
GET    /api/objects/{bucket}/download        → ZIP download; accepts ?prefix= to scope to a virtual folder

# Auth (no auth required)
POST   /account/login     → form login (sets cookie), redirects to returnUrl
GET    /account/logout    → clears cookie, redirects to /login

# System
GET    /health            → liveness (200 if process is up, no checks); also at /health/live
GET    /health/ready      → readiness (checks DB connectivity + blob storage writability)
GET    /hangfire          → Hangfire dashboard (Admin role or localhost)
GET    /hangfire          → Hangfire dashboard (Admin role or localhost)

# S3-Compatible API — port 9000 (AWS Signature V4 required)
# Single shared RouteGroupBuilder: app.MapGroup("/").RequireHost("*:9000").RequireAuthorization()
# Auth: SigV4AuthMiddleware runs before UseAuthorization, sets context.User on valid signature
GET    /                        → list all buckets (S3 ListAllMyBuckets XML)
HEAD   /{bucket}                → bucket exists check (200/404)
PUT    /{bucket}                → create bucket (S3 XML response)
DELETE /{bucket}                → delete bucket
PUT    /{bucket}/{*key}         → upload object (returns ETag header)
PUT    /{bucket}/{*key}?partNumber=N&uploadId=X → UploadPart; upserts part, returns ETag header
GET    /{bucket}/{*key}         → download object; ?download=true forces application/octet-stream attachment; Range requests supported
GET    /{bucket}/{*key}?uploadId=X → ListParts XML
HEAD   /{bucket}/{*key}         → object metadata (ETag, Content-Length, Content-Type headers)
DELETE /{bucket}/{*key}         → delete object (204)
DELETE /{bucket}/{*key}?uploadId=X → AbortMultipartUpload; deletes parts + session
POST   /{bucket}/{*key}?uploads → InitiateMultipartUpload; returns UploadId XML
POST   /{bucket}/{*key}?uploadId=X → CompleteMultipartUpload; assembles parts, saves object, returns ETag

# S3 implementation details:
# - SigV4Parser (ObjeX.Api/S3/SigV4Parser.cs) — parses Authorization header + presigned query params
# - SigV4Signer (ObjeX.Api/S3/SigV4Signer.cs) — canonical request, string-to-sign, HMAC key derivation
# - SigV4AuthMiddleware (ObjeX.Api/Middleware/) — orchestrates auth: lookup → timestamp → sig → payload hash
# - S3Xml helper (ObjeX.Api/S3/S3Xml.cs) — XML response builders, SecurityElement.Escape() for injection prevention
# - S3Errors constants (ObjeX.Api/S3/S3Errors.cs) — S3 error code strings
# - S3MultipartEndpoint (ObjeX.Api/Endpoints/S3Endpoints/) — Initiate + Complete (single MapPost dispatch on ?uploads vs ?uploadId)
# - Parts stored at {BasePath}/_multipart/{uploadId}/{partNumber}.part; cleaned up after Complete or Abort
# - Final ETag: MD5(binary concat of part MD5 bytes) + "-" + partCount (S3 multipart format)
# - CleanupAbandonedMultipartJob — weekly Hangfire job, deletes uploads older than 7 days
# - UI single-file downloads use /api/objects/{bucket}/{*key}?download=true on port 9001 (cookie auth)
# - ZIP downloads use /api/objects/{bucket}/download?prefix= on port 9001
# - Presigned URL generation: GET /api/presign/{bucket}/{*key}?expires=N (port 9001, cookie auth)
#   → PresignedUrlGenerator (ObjeX.Core/Utilities/) — pure BCL, no ASP.NET dependency
#   → Expiry defaults/max: stored in SystemSettings DB row (Id=1); configurable via Settings UI — NOT via appsettings/env vars
#   → UI: link icon button opens PresignedUrlDialog — chip presets + custom number/unit input, live expiry preview
```

---

## Key NuGet Packages

| Package | Used for |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite persistence |
| `EFCore.NamingConventions` | snake_case DB columns |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | User auth, password hashing, roles |
| `Serilog.AspNetCore` | Structured request logging |
| `Hangfire.Core` | Background job scheduling |
| `Hangfire.AspNetCore` | Hangfire DI + ASP.NET Core host integration |
| `Hangfire.Storage.SQLite` | Hangfire job store (reuses `objex.db`) |
| `Radzen.Blazor` | UI component library |

---

## CI/CD

**`ci.yml`** — build gate, GitHub-hosted runner (`ubuntu-latest`). Triggers on push to `main` and all PRs. Steps: checkout → setup .NET (from `global.json`) → restore → build Release. No tests yet.

**`cd.yml`** — triggers on push to `main`. Builds multi-arch image (amd64/arm64) via Buildx + QEMU and pushes to Docker Hub (`meritonaliu/objex:latest` + `meritonaliu/objex:<sha>`). Requires `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` repository secrets.

**`.github/dependabot.yml`** — weekly Monday PRs for NuGet packages (grouped: `radzen`, `ef-core`, `hangfire`, `serilog`, max 5 open) and GitHub Actions versions.

**`.dockerignore`** is present at repo root. It excludes `src/**/bin/`, `src/**/obj/`, `data/`, `.git/`, IDE folders, and local config overrides (`appsettings.Development.json`). Without it, `docker build` would send ~230MB of build artifacts as context on every build.

---

## Run Locally

```bash
cd src/ObjeX.Api
dotnet run
# → http://localhost:9001  (login: admin / admin)
# → http://localhost:9001/hangfire   (job dashboard)
# → http://localhost:9001/health
```

## EF Migrations

```bash
cd src/ObjeX.Api
dotnet ef migrations add <MigrationName> --project ../ObjeX.Infrastructure
dotnet ef database update  # or just run the app — auto-migrates
```

---

## Roadmap (Priority Order)

1. ~~**Dockerize**~~ ✅ — multi-stage Dockerfile, docker-compose, volume mounts, multi-arch
2. ~~**Object listing with prefix/delimiter**~~ ✅ — virtual folder nav, New Folder, ZIP download, folder delete
3. ~~**S3 Compatibility**~~ ✅ — `/{bucket}/{key}` routes, XML responses, AWS Sig V4, S3 error codes
4. ~~**Multipart Upload**~~ ✅ — Initiate/UploadPart/Complete/Abort, part storage, multipart ETag, Range support, abandoned upload cleanup
5. ~~**Presigned URLs**~~ ✅ — GET presigned URLs, configurable expiry, copy-link UI with duration picker
6. ~~**Enhanced Blazor UI**~~ ✅ — folder nav, dark mode (system preference + cookie persistence)
7. **Object Tags** — key-value tags, tag-based search, lifecycle/retention policies
8. **User Management UI** — registration, user list, password reset (Identity backend already in place)
9. **Bucket Permissions** — per-bucket ACL, read/write/delete, permission checks in endpoints
10. **Teams/Orgs** — multi-tenant, org workspaces, team roles, storage quotas
11. **Storage backends** — swap `FileSystemStorageService` for cloud or chunked storage
12. **PostgreSQL support** — swap SQLite via same `IMetadataService` interface
