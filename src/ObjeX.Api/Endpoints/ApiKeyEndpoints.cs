using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/keys").WithTags("API Keys");

        group.MapPost("/", async (HttpContext ctx, ObjeXDbContext db, CreateApiKeyRequest req) =>
        {
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);

            var apiKey = new ApiKey
            {
                Name = req.Name,
                UserId = userId!,
                ExpiresAt = req.ExpiresInDays.HasValue
                    ? DateTime.UtcNow.AddDays(req.ExpiresInDays.Value)
                    : DateTime.UtcNow.AddYears(10)
            };

            db.ApiKeys.Add(apiKey);
            await db.SaveChangesAsync();

            return Results.Ok(new { apiKey.Key, apiKey.Name, apiKey.ExpiresAt });
        });

        group.MapGet("/", async (HttpContext ctx, ObjeXDbContext db) =>
        {
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var keys = await db.ApiKeys
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new { k.Id, k.Name, k.ExpiresAt, k.LastUsedAt, k.CreatedAt })
                .ToListAsync();

            return Results.Ok(keys);
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, ObjeXDbContext db) =>
        {
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);

            if (apiKey is null) return Results.NotFound();

            db.ApiKeys.Remove(apiKey);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        return group;
    }
}

public record CreateApiKeyRequest(string Name, int? ExpiresInDays);
