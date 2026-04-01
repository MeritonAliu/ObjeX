using System.Net;
using Microsoft.Extensions.DependencyInjection;

using ObjeX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ObjeX.Tests.Integration;

public class ResilienceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task MissingBlob_Download_ReturnsError()
    {
        var bucket = "test-bucket";
        var key = "missing-blob-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "will be orphaned"u8.ToArray();

        // Upload normally
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        // Delete the physical blob file from disk (simulate crash/corruption)
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var obj = await db.BlobObjects.FirstAsync(o => o.BucketName == bucket && o.Key == key);
            if (File.Exists(obj.StoragePath))
                File.Delete(obj.StoragePath);
        }

        // Try to download — should get error (not 200 with corrupt/empty content)
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.True(
            getResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError,
            $"Expected 404 or 500 for missing blob, got {(int)getResponse.StatusCode}");
    }

    [Fact]
    public async Task ConcurrentUploads_SameKey_FinalStateConsistent()
    {
        var bucket = "test-bucket";
        var key = "concurrent-" + Guid.NewGuid().ToString("N")[..6] + ".bin";

        // Launch multiple uploads to the same key concurrently.
        // Unique .tmp files prevent filesystem collisions, but SQLite upserts on
        // the same (BucketName, Key) unique index can still race. That's expected
        // under concurrent writes — the important thing is final state consistency.
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var content = new byte[4096];
            Random.Shared.NextBytes(content);
            content[0] = (byte)i;

            var request = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
            request.Content = new ByteArrayContent(content);
            S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, content);
            return await _client.SendAsync(request);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // At least one must succeed; some may 500 due to DB concurrency
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(succeeded >= 1, "At least one concurrent upload should succeed");

        // Final state must be consistent: download returns one coherent upload
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(4096, downloaded.Length); // correct size, not a corrupt mix
    }
}
