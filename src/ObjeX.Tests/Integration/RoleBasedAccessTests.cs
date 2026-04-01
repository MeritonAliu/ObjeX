using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

public class RoleBasedAccessTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task UserRole_CanOnlySeeOwnBuckets()
    {
        // Create a regular User with their own credential and bucket
        var (userId, accessKeyId, secretKey) = await CreateUserWithCredential("iso-user-a");
        var ownBucket = "iso-user-a-bucketxx";

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            db.Buckets.Add(new Bucket { Name = ownBucket, OwnerId = userId });
            await db.SaveChangesAsync();
        }

        // User can see their own bucket
        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(listRequest, accessKeyId, secretKey);
        var listResponse = await _client.SendAsync(listRequest);
        var xml = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(ownBucket, xml);

        // User should NOT see admin's test-bucket in their list
        // (test-bucket is owned by admin, iso-user-a is User role)
        Assert.DoesNotContain("test-bucket", xml);
    }

    [Fact]
    public async Task UserRole_CannotAccessOtherUsersBucket()
    {
        var (_, accessKeyId, secretKey) = await CreateUserWithCredential("iso-user-b");

        // Try to HEAD admin's bucket — should fail (not owned by this user)
        var headRequest = new HttpRequestMessage(HttpMethod.Head, "/test-bucket");
        S3RequestSigner.SignRequest(headRequest, accessKeyId, secretKey);
        var response = await _client.SendAsync(headRequest);
        // GetBucketAsync with ownerFilter returns null for non-owned → 404
        // (may also get 403 in some auth edge cases)
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404/403 for non-owned bucket, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task AdminViaS3_SeesAllBuckets()
    {
        var (userId, _, _) = await CreateUserWithCredential("iso-user-c");
        var userBucket = "iso-user-c-bucketxx";

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            if (!await db.Buckets.AnyAsync(b => b.Name == userBucket))
            {
                db.Buckets.Add(new Bucket { Name = userBucket, OwnerId = userId });
                await db.SaveChangesAsync();
            }
        }

        // Admin via S3 now sees all buckets (role claims added to SigV4 identity)
        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var listResponse = await _client.SendAsync(listRequest);
        var xml = await listResponse.Content.ReadAsStringAsync();

        Assert.Contains("test-bucket", xml);
        Assert.Contains(userBucket, xml);
    }

    private async Task<(string UserId, string AccessKeyId, string SecretKey)> CreateUserWithCredential(string username)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
        {
            var existingCred = await db.S3Credentials.FirstAsync(c => c.UserId == existing.Id);
            return (existing.Id, existingCred.AccessKeyId, existingCred.SecretAccessKey);
        }

        var user = new User
        {
            UserName = username,
            Email = $"{username}@test.local",
            EmailConfirmed = true,
            StorageUsedBytes = 0
        };
        var result = await userManager.CreateAsync(user, "test1234");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, "User");

        var (credential, secret) = S3Credential.Create($"{username}-key", user.Id);
        db.S3Credentials.Add(credential);
        await db.SaveChangesAsync();

        return (user.Id, credential.AccessKeyId, secret);
    }
}
