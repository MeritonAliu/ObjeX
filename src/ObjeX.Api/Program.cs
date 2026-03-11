using Hangfire;
using Hangfire.Storage.SQLite;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using ObjeX.Api.Auth;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Hashing;
using ObjeX.Infrastructure.Health;
using ObjeX.Infrastructure.Jobs;
using ObjeX.Infrastructure.Metadata;
using ObjeX.Infrastructure.Storage;
using ObjeX.Web.Components;
using ObjeX.Api.Endpoints;
using ObjeX.Api.Middleware;
using ObjeX.Core.Models;

using Radzen;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IMetadataService, SqliteMetadataService>();
builder.Services.AddSingleton<IHashService, Sha256HashService>();
builder.Services.AddSingleton<FileSystemStorageService>(sp =>
{
    var basePath = builder.Configuration["Storage:BasePath"] ?? "./data/blobs";
    basePath = Path.GetFullPath(basePath);
    return new FileSystemStorageService(basePath, sp.GetRequiredService<IHashService>(), sp.GetRequiredService<ILogger<FileSystemStorageService>>());
});
builder.Services.AddSingleton<IObjectStorageService>(sp => sp.GetRequiredService<FileSystemStorageService>());
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddOpenApi();
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
    })
    .AddEntityFrameworkStores<ObjeXDbContext>()
    .AddDefaultTokenProviders();


builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
.AddBearerToken(IdentityConstants.BearerScheme);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddSingleton<IEmailSender<User>, NoOpEmailSender>();

builder.Services.ConfigureApplicationCookie(options =>
{
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


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=./data/db/objex.db";
// Resolve relative path from CWD at startup and lock it to absolute
// so it stays stable regardless of any later CWD changes.
string dbFilePath = Path.GetFullPath(connectionString.Replace("Data Source=", "").Trim());
connectionString = $"Data Source={dbFilePath}";

builder.Services.AddDbContext<ObjeXDbContext>(options =>
{
    options.UseSqlite(connectionString);
    options.UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

builder.Services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(dbFilePath));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<CleanupOrphanedBlobsJob>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 500 * 1024 * 1024);
builder.Services.AddHttpContextAccessor();
builder.Services.AddRadzenComponents();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    Directory.CreateDirectory(Path.GetDirectoryName(dbFilePath)!);
    db.Database.Migrate();
    
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
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
                await roleManager.CreateAsync(new IdentityRole("User"));
            }
            await userManager.AddToRoleAsync(adminUser, "Admin");

            app.Logger.LogWarning(
                "⚠️  Default admin user created: {Username} {Email} / {Password} - CHANGE THIS IN PRODUCTION!",
                defaultUsername, defaultEmail, defaultPassword);
        }
    }
}

app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found"));
app.UseStaticFiles();
app.UseCors();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (!app.Environment.IsDevelopment())
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
    await next();
});
app.UseAuthentication();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
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
app.UseResponseCompression();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

RecurringJob.AddOrUpdate<CleanupOrphanedBlobsJob>(
    "cleanup-orphaned-blobs",
    job => job.ExecuteAsync(),
    Cron.Weekly(DayOfWeek.Sunday, 3)); // weekly Sunday 03:00 UTC

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("ObjeX API");
}).RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.MapBucketEndpoints().RequireAuthorization("ApiPolicy");
app.MapObjectEndpoints().RequireAuthorization("ApiPolicy");
app.MapApiKeyEndpoints().RequireAuthorization("ApiPolicy");
app.MapIdentityApi<User>();

app.MapAccountEndpoints();

app.Run();