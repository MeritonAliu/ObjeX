using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Storage.SQLite;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Hashing;
using ObjeX.Infrastructure.Health;
using ObjeX.Infrastructure.Jobs;
using ObjeX.Infrastructure.Metadata;
using ObjeX.Infrastructure.Storage;
using ObjeX.Web.Components;
using ObjeX.Api.Auth;
using ObjeX.Api.Endpoints;
using ObjeX.Api.Endpoints.S3Endpoints;
using ObjeX.Api.Middleware;
using ObjeX.Core.Models;

using Prometheus;
using Radzen;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Upload size limit — null = unlimited (disk space guard is the real protection).
// Override via Storage:MaxUploadBytes in config.
var maxUploadBytes = builder.Configuration.GetValue<long?>("Storage:MaxUploadBytes");
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxUploadBytes;
    o.AddServerHeader = false; // don't leak "Server: Kestrel"
    o.ListenAnyIP(9001); // UI 
    o.ListenAnyIP(9000); // S3-compatible API
});

builder.Services.AddScoped<IMetadataService, SqliteMetadataService>();
builder.Services.AddSingleton<IHashService, Sha256HashService>();
builder.Services.AddSingleton<FileSystemStorageService>(sp =>
{
    var basePath = builder.Configuration["Storage:BasePath"] ?? "./data/blobs";
    basePath = Path.GetFullPath(basePath);
    return new FileSystemStorageService(basePath, sp.GetRequiredService<IHashService>(), sp.GetRequiredService<ILogger<FileSystemStorageService>>());
});
builder.Services.AddSingleton<IObjectStorageService>(sp => sp.GetRequiredService<FileSystemStorageService>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ObjeXDbContext>(tags: ["ready"])
    .AddCheck<BlobStorageHealthCheck>("blob_storage", tags: ["ready"]);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddIdentity<User, IdentityRole>(options =>
    {
        // Relax password requirements for MVP
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 4;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ObjeXDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<UserClaimsPrincipalFactory<User, IdentityRole>>();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));


builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
});

builder.Services.AddAuthorization();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});


var databaseProvider = builder.Configuration["Database:Provider"]?.ToLowerInvariant()
                      ?? builder.Configuration["DATABASE_PROVIDER"]?.ToLowerInvariant()
                      ?? "sqlite";

if (databaseProvider is not "sqlite" and not "postgresql")
    throw new InvalidOperationException($"Invalid database provider '{databaseProvider}'. Supported values: sqlite, postgresql. Set via DATABASE_PROVIDER env var or Database:Provider in config.");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (databaseProvider == "sqlite")
{
    connectionString ??= "Data Source=./data/db/objex.db";
}
else if (string.IsNullOrEmpty(connectionString) || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"ConnectionStrings:DefaultConnection must be a PostgreSQL connection string when Database:Provider=postgresql. " +
        $"Example: Host=localhost;Database=objex;Username=objex;Password=secret");
}

string? dbFilePath = null;
if (databaseProvider == "sqlite")
{
    // Resolve relative path from CWD at startup and lock it to absolute
    // so it stays stable regardless of any later CWD changes.
    dbFilePath = Path.GetFullPath(connectionString.Replace("Data Source=", "").Trim());
    connectionString = $"Data Source={dbFilePath}";
}

builder.Services.AddDbContext<ObjeXDbContext>(options =>
{
    if (databaseProvider == "postgresql")
        options.UseNpgsql(connectionString, o =>
        {
            o.CommandTimeout(30);
            o.MigrationsAssembly("ObjeX.Migrations.PostgreSql");
        });
    else
        options.UseSqlite(connectionString, o => o.CommandTimeout(30));

    options.UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

if (databaseProvider == "postgresql")
{
    builder.Services.AddHangfire(config => config
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connectionString)));
}
else
{
    builder.Services.AddHangfire(config => config
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSQLiteStorage(dbFilePath!));
}
builder.Services.AddHangfireServer();
if (builder.Configuration.GetValue<bool>("Metrics:Enabled"))
    builder.Services.AddHostedService<ObjeX.Api.Metrics.BucketMetricsSyncJob>();
builder.Services.AddScoped<CleanupOrphanedBlobsJob>();
builder.Services.AddScoped<VerifyBlobIntegrityJob>();
builder.Services.AddScoped<CleanupAbandonedMultipartJob>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 500 * 1024 * 1024);
builder.Services.AddHttpContextAccessor();
builder.Services.AddRadzenComponents();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddCors(options =>
{
    options.AddPolicy("S3", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login: 5 attempts per 2 minutes per IP — brute-force protection
    options.AddSlidingWindowLimiter("login", o =>
    {
        o.Window = TimeSpan.FromMinutes(2);
        o.SegmentsPerWindow = 4;
        o.PermitLimit = 5;
        o.QueueLimit = 0;
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.NewestFirst;
    });

    // API key creation: 10 per minute per IP — sensitive write
    options.AddFixedWindowLimiter("key-create", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 10;
        o.QueueLimit = 0;
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.NewestFirst;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    if (databaseProvider == "sqlite")
        Directory.CreateDirectory(Path.GetDirectoryName(dbFilePath!)!);

    app.Logger.LogInformation("Database provider: {Provider}", databaseProvider);

    var autoMigrate = builder.Configuration.GetValue<bool>("Database:AutoMigrate", defaultValue: true);
    if (autoMigrate)
    {
        app.Logger.LogWarning("Running database migrations on startup. Ensure a backup exists before migrating in production. Set Database:AutoMigrate=false to disable.");
        db.Database.Migrate();
    }
    else
    {
        app.Logger.LogInformation("Database:AutoMigrate is disabled — skipping automatic migrations.");
    }

    if (databaseProvider == "sqlite")
    {
        // Enable WAL mode, synchronous=NORMAL, and busy_timeout — persisted to the DB file.
        // WAL allows concurrent reads during writes; NORMAL reduces fsync overhead safely.
        // busy_timeout retries for 5s on SQLITE_BUSY before throwing.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
    }
    
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var roleName in new[] { "Admin", "Manager", "User" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole(roleName));
    }

    if (await userManager.FindByNameAsync("admin") is null)
    {
        var defaultUsername = builder.Configuration["DefaultAdmin:Username"] ?? "admin";
        var defaultEmail = builder.Configuration["DefaultAdmin:Email"] ?? "admin@objex.local";
        var defaultPassword = builder.Configuration["DefaultAdmin:Password"] ?? "admin";

        var adminUser = new User
        {
            UserName = defaultUsername,
            Email = defaultEmail,
            EmailConfirmed = true,
            StorageUsedBytes = 0 // TODO: make 0 default in model property
        };

        var result = await userManager.CreateAsync(adminUser, defaultPassword);

        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");

            app.Logger.LogWarning(
                "⚠️  Default admin user created. Change the credentials before exposing this instance publicly " +
                "(appsettings.json: DefaultAdmin:Password — Docker: DefaultAdmin__Password).");
        }
    }

    // Seed S3 credential and buckets from config (idempotent, skipped if already exists)
    var seedAccessKey = builder.Configuration["Seed:S3Credential:AccessKeyId"];
    var seedSecretKey = builder.Configuration["Seed:S3Credential:SecretAccessKey"];
    var seedBuckets = builder.Configuration["Seed:Buckets"];
    var needsSeeding = (!string.IsNullOrWhiteSpace(seedAccessKey) && !string.IsNullOrWhiteSpace(seedSecretKey))
                       || !string.IsNullOrWhiteSpace(seedBuckets);

    if (needsSeeding)
    {
        var adminUser = await userManager.FindByNameAsync(
            builder.Configuration["DefaultAdmin:Username"] ?? "admin");

        if (adminUser is null)
        {
            app.Logger.LogWarning("Cannot seed S3 credential or buckets: admin user not found.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(seedAccessKey) && !string.IsNullOrWhiteSpace(seedSecretKey)
                && !await db.S3Credentials.AnyAsync(c => c.AccessKeyId == seedAccessKey))
            {
                var credentialName = builder.Configuration["Seed:S3Credential:Name"] ?? "seed-credential";
                db.S3Credentials.Add(new S3Credential
                {
                    Name = credentialName,
                    AccessKeyId = seedAccessKey,
                    SecretAccessKey = seedSecretKey,
                    UserId = adminUser.Id
                });
                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded S3 credential '{Name}' (AccessKeyId: {AccessKeyId})",
                    credentialName, seedAccessKey);
            }

            if (!string.IsNullOrWhiteSpace(seedBuckets))
            {
                var metadataService = scope.ServiceProvider.GetRequiredService<IMetadataService>();
                foreach (var bucketName in seedBuckets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (await db.Buckets.AnyAsync(b => b.Name == bucketName))
                        continue;

                    var error = ObjeX.Core.Validation.BucketNameValidator.GetValidationError(bucketName);
                    if (error is not null)
                    {
                        app.Logger.LogWarning("Skipping invalid seed bucket '{Name}': {Error}", bucketName, error);
                        continue;
                    }

                    await metadataService.CreateBucketAsync(new Bucket { Name = bucketName, OwnerId = adminUser.Id });
                    app.Logger.LogInformation("Seeded bucket '{Name}'", bucketName);
                }
            }
        }
    }

    if (builder.Configuration.GetValue<bool>("Metrics:Enabled"))
    {
        foreach (var bucket in await db.Buckets.ToListAsync())
            ObjeX.Api.Metrics.ObjeXMetrics.SetBucketStats(bucket.Name, bucket.TotalSize, bucket.ObjectCount);
    }
}

app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithRedirects("/not-found"));
app.UseStaticFiles();
app.UseWhen(
    ctx => ctx.Connection.LocalPort == 9000,
    branch => branch.UseCors("S3"));
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Remove("X-Powered-By");
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (!app.Environment.IsDevelopment())
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
    await next();
});
app.UseAuthentication();
// SigV4 auth runs only on port 9000 (S3 API) — must be after UseAuthentication
app.UseWhen(ctx => ctx.Connection.LocalPort == 9000, branch =>
    branch.UseMiddleware<SigV4AuthMiddleware>());
app.UseAuthorization();

app.UseAntiforgery();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An error occurred" });
        });
    });
}
app.UseSerilogRequestLogging();
var metricsEnabled = builder.Configuration.GetValue<bool>("Metrics:Enabled");
if (metricsEnabled)
    app.UseHttpMetrics();
app.UseResponseCompression();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

RecurringJob.AddOrUpdate<CleanupOrphanedBlobsJob>(
    "cleanup-orphaned-blobs",
    job => job.ExecuteAsync(),
    Cron.Weekly(DayOfWeek.Sunday, 3)); // weekly Sunday 03:00 UTC

RecurringJob.AddOrUpdate<VerifyBlobIntegrityJob>(
    "verify-blob-integrity",
    job => job.ExecuteAsync(),
    Cron.Weekly(DayOfWeek.Sunday, 4)); // weekly Sunday 04:00 UTC (1h after cleanup)

RecurringJob.AddOrUpdate<CleanupAbandonedMultipartJob>(
    "cleanup-abandoned-multipart",
    job => job.ExecuteAsync(),
    Cron.Weekly(DayOfWeek.Sunday, 5)); // weekly Sunday 05:00 UTC

if (metricsEnabled)
    app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDownloadEndpoints();
app.MapPresignEndpoints();

// S3 Endpoints — port 9000 only, auth via SigV4AuthMiddleware above
var s3Group = app.MapGroup("/").RequireHost("*:9000").RequireAuthorization();
app.MapS3BucketEndpoints(s3Group);
app.MapS3ObjectEndpoints(s3Group);
app.MapS3MultipartEndpoints(s3Group);
app.MapS3PostObjectEndpoints(s3Group);

app.MapAccountEndpoints();

app.Run();