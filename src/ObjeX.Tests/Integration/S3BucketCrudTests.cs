using System.Net;

namespace ObjeX.Tests.Integration;

public class S3BucketCrudTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task CreateBucket_ListBuckets_HeadBucket_DeleteBucket()
    {
        var bucket = "crud-lifecycle-" + Guid.NewGuid().ToString("N")[..6] + "xx";

        // Create
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        // List — should contain the bucket
        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var listResponse = await _client.SendAsync(listRequest);
        var listXml = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(bucket, listXml);

        // HEAD
        var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/{bucket}");
        S3RequestSigner.SignRequest(headRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var headResponse = await _client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // HEAD after delete — should fail (404 or 403 for non-existent bucket)
        var headAfterDelete = new HttpRequestMessage(HttpMethod.Head, $"/{bucket}");
        S3RequestSigner.SignRequest(headAfterDelete, factory.AccessKeyId, factory.SecretAccessKey);
        var headDeletedResponse = await _client.SendAsync(headAfterDelete);
        Assert.True(
            headDeletedResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403 for deleted bucket, got {(int)headDeletedResponse.StatusCode}");
    }

    [Fact]
    public async Task CreateBucket_Duplicate_ReturnsError()
    {
        var bucket = "dup-bucket-" + Guid.NewGuid().ToString("N")[..6] + "xx";

        var put1 = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(put1, factory.AccessKeyId, factory.SecretAccessKey);
        var response1 = await _client.SendAsync(put1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var put2 = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(put2, factory.AccessKeyId, factory.SecretAccessKey);
        var response2 = await _client.SendAsync(put2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var body = await response2.Content.ReadAsStringAsync();
        Assert.Contains("BucketAlreadyExists", body);

        // Cleanup
        var del = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}");
        S3RequestSigner.SignRequest(del, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(del);
    }

    [Fact]
    public async Task CreateBucket_InvalidName_ReturnsError()
    {
        var putRequest = new HttpRequestMessage(HttpMethod.Put, "/AB");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(putRequest);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InvalidBucketName", body);
    }

    [Fact]
    public async Task DeleteBucket_NonEmpty_Returns409()
    {
        var bucket = "nonempty-" + Guid.NewGuid().ToString("N")[..6] + "xx";

        // Create bucket
        var createBucket = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(createBucket, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(createBucket);

        // Upload an object
        var content = "data"u8.ToArray();
        var putObj = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/file.txt");
        putObj.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putObj, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putObj);

        // Try to delete bucket — should fail
        var deleteBucket = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}");
        S3RequestSigner.SignRequest(deleteBucket, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(deleteBucket);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("BucketNotEmpty", body);

        // Cleanup: delete object then bucket
        var delObj = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}/file.txt");
        S3RequestSigner.SignRequest(delObj, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(delObj);
        var delBucket = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}");
        S3RequestSigner.SignRequest(delBucket, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(delBucket);
    }
}
