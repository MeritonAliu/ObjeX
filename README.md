# ObjeX - S3-Compatible Blob Storage (PoC)

**Goal**: Self-hostable, open-source blob storage with S3-compatible API  
**Stack**: .NET API + Blazor UI + Single Docker Container  
**Deployment**: Self-contained, single container with volume mounts

---

## Architecture Overview

```md
┌─────────────────────────────────────┐
│         Single Container            │
│  ┌───────────────────────────────┐  │
│  │   ASP.NET Core App            │  │
│  │                               │  │
│  │  ├─ API Controllers (/api/*)  │  │
│  │  ├─ Blazor Server (/)         │  │
│  │  └─ Static Files              │  │
│  └───────────────────────────────┘  │
│                                     │
│  ┌───────────────────────────────┐  │
│  │   Storage Layer               │  │
│  │   /data/blobs (volume mount)  │  │
│  │   /data/metadata.db (SQLite)  │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
         ▲
         │ Volume Mount
         ▼
    Host: /var/lib/blobstore
```

---

## Project Structure

```md
BlobStore/
├── src/
│   ├── BlobStore.Api/              # ASP.NET Core host
│   │   ├── Controllers/            # S3-compatible endpoints
│   │   ├── Middleware/             # Auth, logging
│   │   └── Program.cs
│   │
│   ├── BlobStore.Web/              # Blazor Server UI
│   │   ├── Pages/                  # Bucket browser, upload UI
│   │   ├── Components/
│   │   └── _Imports.razor
│   │
│   ├── BlobStore.Core/             # Domain logic
│   │   ├── Models/                 # Bucket, BlobObject, MultipartUpload
│   │   ├── Interfaces/             # IStorageEngine, IMetadataStore
│   │   └── Services/               # ObjectManager, BucketService
│   │
│   └── BlobStore.Infrastructure/   # Implementation
│       ├── Storage/                # FileSystemStorageEngine
│       ├── Metadata/               # SqliteMetadataStore
│       └── Security/               # Simple key-based auth
│
├── Dockerfile
├── docker-compose.yml
└── README.md
```

---

## Technology Stack

### Backend

- **ASP.NET Core 8** (single app hosting both API + Blazor)
- **Minimal APIs** for S3 endpoints (lightweight, fast)
- **Blazor Server** for admin UI (no separate SPA build complexity)
- Single process, shared DI container

### UI

- **Blazor Server + MudBlazor**
- MudBlazor = professional components out of the box
- File upload with progress bars
- Bucket/object tree view
- Server-side = no CORS issues, simpler auth

### Storage

- **Blobs**: Filesystem `/data/blobs/{bucket}/{hash-prefix}/{object-key}`
- **Metadata**: SQLite at `/data/metadata.db`
- Both in Docker volume for persistence

### Auth (PoC)

- Simple API key in config/env var
- Header: `X-API-Key: your-secret-key`
- Later: AWS Signature V4 for S3 compatibility

---

## MVP Features

### S3 API (Phase 1)

- `PUT /{bucket}/{key}` - Upload object
- `GET /{bucket}/{key}` - Download object
- `DELETE /{bucket}/{key}` - Delete object
- `HEAD /{bucket}/{key}` - Get metadata
- `GET /{bucket}?list-type=2` - List objects
- `PUT /{bucket}` - Create bucket
- `DELETE /{bucket}` - Delete bucket

### Blazor UI (Phase 1)

- Dashboard: Total storage, object count, bucket list
- Bucket browser: Navigate folders, upload files
- Object viewer: Download, delete, metadata display
- Settings: API key management

---

## Core Interfaces (Clean Architecture)

### IStorageEngine

```csharp
// BlobStore.Core/Interfaces/IStorageEngine.cs
public interface IStorageEngine
{
    Task<string> StoreAsync(string bucket, string key, Stream data, CancellationToken ct);
    Task<Stream> RetrieveAsync(string bucket, string key, CancellationToken ct);
    Task DeleteAsync(string bucket, string key, CancellationToken ct);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct);
}
```

### IMetadataStore

```csharp
// BlobStore.Core/Interfaces/IMetadataStore.cs
public interface IMetadataStore
{
    Task<BlobObject?> GetObjectAsync(string bucket, string key, CancellationToken ct);
    Task SaveObjectAsync(BlobObject obj, CancellationToken ct);
    Task<IEnumerable<BlobObject>> ListObjectsAsync(string bucket, string? prefix, CancellationToken ct);
}
```

### DI Registration (Program.cs)

```csharp
// Storage
builder.Services.AddSingleton<IStorageEngine, FileSystemStorageEngine>();
builder.Services.AddSingleton<IMetadataStore, SqliteMetadataStore>();

// Business logic
builder.Services.AddScoped<ObjectManager>();
builder.Services.AddScoped<BucketService>();

// Both API and Blazor
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
```

---

## Dockerfile

```dockerfile
# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy and restore
COPY src/ .
RUN dotnet restore BlobStore.Api/BlobStore.Api.csproj

# Build
RUN dotnet publish BlobStore.Api/BlobStore.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Create data directory
RUN mkdir -p /data/blobs && \
    chmod 755 /data

COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s \
    CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "BlobStore.Api.dll"]
```

---

## Docker Compose (Self-Hosting)

```yaml
version: '3.8'

services:
  blobstore:
    image: yourusername/blobstore:latest
    container_name: blobstore
    ports:
      - "8080:8080"
    environment:
      - BLOBSTORE_API_KEY=change-me-in-production
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - blobstore-data:/data
    restart: unless-stopped

volumes:
  blobstore-data:
```

---

## Development Workflow

### Local Development

```bash
cd src/BlobStore.Api
dotnet run
```

### Docker Build

```bash
docker build -t blobstore:dev .
```

### Run Container

```bash
docker run -p 8080:8080 \
  -v $(pwd)/data:/data \
  -e BLOBSTORE_API_KEY=dev-key \
  blobstore:dev
```

### Test S3 API

```bash
# Create bucket
curl -X PUT http://localhost:8080/mybucket \
  -H "X-API-Key: dev-key"

# Upload object
curl -X PUT http://localhost:8080/mybucket/test.txt \
  -H "X-API-Key: dev-key" \
  --data "Hello World"

# Download object
curl http://localhost:8080/mybucket/test.txt \
  -H "X-API-Key: dev-key"
```

---

## Growth Path (Architectural Extensibility)

### Phase 1 → Phase 2: Add Features

- Multipart upload (new controller, same storage engine)
- AWS Signature V4 (new middleware)
- Presigned URLs (token service)

### Phase 2 → Phase 3: Extract When Needed

- **Storage backends**: Swap `FileSystemStorageEngine` for `S3StorageEngine` or `ChunkedStorageEngine`
- **Metadata**: Swap SQLite for PostgreSQL via same interface
- **Distributed**: Keep API, replace storage layer with distributed coordination

### Phase 3 → Microservices (if ever needed)

- API Gateway (same contracts)
- Storage Service (gRPC)
- Metadata Service (separate DB)
- UI becomes standalone SPA

*Won't need this until real scale problems*

---

## Technical Challenges to Learn

### 1. Storage Backend Architecture

- Naive: Direct filesystem storage
- Challenge: Millions of small files kill filesystem performance
- Solution: Chunking strategies, metadata separation, object packing
- Trade-off: Simple filesystems (ext4/XFS) vs custom append-log

### 2. Metadata Management

- Separate object metadata from blob data
- Fast listing operations (S3 ListObjects with prefixes/delimiters)
- Options: SQLite/RocksDB, PostgreSQL, custom indexing

### 3. Durability & Consistency

- Erasure coding for redundancy (Reed-Solomon)
- Replication strategies for multi-node
- Read-after-write consistency
- Checksumming (MD5/SHA256) for corruption detection

### 4. Multipart Upload Protocol

- Stateful: InitiateMultipartUpload → UploadPart → CompleteMultipartUpload
- Track in-progress uploads, handle part ETags
- Atomic assembly of final object
- Garbage collection of abandoned uploads

### 5. Performance & Scalability

- Streaming (don't buffer entire objects in memory)
- Concurrent access (read/write locking)
- Range requests (HTTP byte-range)
- Horizontal scaling: sharding strategy

### 6. S3 API Compatibility

- AWS Signature V4 (complex HMAC-SHA256 canonical request signing)
- XML response formatting (S3 uses XML, not JSON)
- Presigned URLs (time-limited access tokens)
- Versioning, lifecycle policies, ACLs

---

## What This Teaches

✅ Clean architecture in real project (not toy example)  
✅ Docker packaging for self-hosted apps  
✅ Blazor + API in single ASP.NET Core host  
✅ Blob storage fundamentals (chunking, streaming, metadata)  
✅ Interface-driven design for swappable components  
✅ Open source workflows (versioning, releases, docs)  
✅ Systems engineering (filesystem I/O, resource constraints)  
✅ Backend depth (API design, streaming, concurrency)  
✅ Infrastructure (Proxmox deployment, Linux I/O tuning)

---

## Open Source Setup

- **License**: MIT or Apache 2.0
- **README**: Quick start with `docker run` one-liner
- **Docs**: API compatibility matrix, configuration options
- **CI/CD**: GitHub Actions building multi-arch images (amd64, arm64)

---

## Storage Engine File Structure

```md
/data/
├── blobs/
│   ├── bucket-1/
│   │   ├── ab/
│   │   │   └── ab123...xyz  # Object hashed for distribution
│   │   └── cd/
│   │       └── cd456...xyz
│   └── bucket-2/
│       └── ...
└── metadata.db  # SQLite database
```

---

## Next Steps

1. **Create solution structure**
   - Set up projects (Api, Web, Core, Infrastructure)
   - Add NuGet packages (MudBlazor, EF Core SQLite, etc.)

2. **Implement core interfaces**
   - IStorageEngine → FileSystemStorageEngine
   - IMetadataStore → SqliteMetadataStore

3. **Build minimal API**
   - Basic PUT/GET/DELETE endpoints
   - Simple authentication middleware

4. **Create Blazor UI**
   - Dashboard page
   - Bucket browser
   - Upload component

5. **Containerize**
   - Write Dockerfile
   - Test locally
   - Push to Docker Hub

6. **Deploy on Proxmox**
   - LXC container or VM
   - Test with s3cmd or aws-cli
   - Monitor resource usage

---

## References & Inspirations

- **MinIO**: Study for patterns (deprecated but good reference)
- **SeaweedFS**: Simpler architecture to learn from
- **Garage**: Rust-based, designed for self-hosted setups
- **S3 API Spec**: AWS documentation for compatibility

---

## Performance Targets (MVP)

- Handle 10k+ objects without degradation
- Support files up to 5GB
- <100ms latency for small object GET
- Streaming for large files (no memory buffering)
- Concurrent uploads/downloads (10+ simultaneous)

---

## Questions to Explore

- How does filesystem choice affect performance? (ext4 vs XFS vs btrfs)
- When does chunking become necessary?
- What's the sweet spot for metadata caching?
- How to handle orphaned data after crashes?
- What's the cost of checksumming every object?

---

**Status**: Planning Phase  
**Timeline**: 2-4 weeks for MVP  
**Deployment**: Proxmox homelab (Ubuntu VM/LXC)

