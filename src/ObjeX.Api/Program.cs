using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Metadata;
using ObjeX.Infrastructure.Storage;
using ObjeX.Web.Components;
using ObjeX.Api.Endpoints;

using Radzen;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IMetadataService, SqliteMetadataService>();
builder.Services.AddSingleton<IObjectStorageService>(_ =>
{
    var basePath = builder.Configuration["Storage:BasePath"]
                   ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "blobs");
    return new FileSystemStorageService(basePath);
});
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddDbContext<ObjeXDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrEmpty(connectionString))
    {
        var currentDir = new DirectoryInfo(builder.Environment.ContentRootPath);
        var solutionRoot = currentDir.Parent?.Parent?.FullName
                          ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."));
        var dbPath = Path.Combine(solutionRoot, "objex.db");
        connectionString = $"Data Source={dbPath}";
    }

    options.UseSqlite(connectionString);
    options.UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 500 * 1024 * 1024);
builder.Services.AddRadzenComponents();

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
    db.Database.Migrate();
}
app.UseStaticFiles();
app.UseCors();
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

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("ObjeX API");
});
app.MapHealthChecks("/health");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.MapBucketEndpoints();
app.MapObjectEndpoints();

app.Run();