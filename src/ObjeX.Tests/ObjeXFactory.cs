using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ObjeX.Api;

namespace ObjeX.Tests;

public class ObjeXFactory : WebApplicationFactory<ApiAssemblyMarker>
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"objex-test-{Guid.NewGuid():N}");

    public string AccessKeyId => "OBXTEST12345678901";
    public string SecretAccessKey => "TestSecretKeyForIntegrationTests1234567890ab";

    public string BlobBasePath => Path.Combine(_tempDir, "blobs");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(_tempDir, "test.db");
        Directory.CreateDirectory(_tempDir);

        builder.UseEnvironment("Production"); // avoid dev exception page noise

        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={dbPath}");
        builder.UseSetting("Storage:BasePath", BlobBasePath);
        builder.UseSetting("Database:Provider", "sqlite");
        builder.UseSetting("Metrics:Enabled", "false");
        builder.UseSetting("Seed:S3Credential:AccessKeyId", AccessKeyId);
        builder.UseSetting("Seed:S3Credential:SecretAccessKey", SecretAccessKey);
        builder.UseSetting("Seed:S3Credential:Name", "test-credential");
        builder.UseSetting("Seed:Buckets", "test-bucket");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Fatal");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestPortOverrideStartupFilter>();

            // Remove Hangfire background server — it holds SQLite connections that
            // throw "out of memory" on disposal when the temp DB is cleaned up.
            // Tests don't need background job processing.
            services.RemoveAll<IHostedService>();
        });
    }

    public HttpClient CreateS3Client()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Host = "localhost:9000";
        return client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}

/// <summary>
/// Injects middleware at the start of the pipeline that sets Connection.LocalPort
/// from the Host header, so port-gated middleware (SigV4) and routes (RequireHost)
/// work correctly in TestServer.
/// </summary>
file class TestPortOverrideStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                if (ctx.Request.Host.Port is { } port)
                    ctx.Connection.LocalPort = port;
                await nextMiddleware();
            });
            next(app);
        };
    }
}
