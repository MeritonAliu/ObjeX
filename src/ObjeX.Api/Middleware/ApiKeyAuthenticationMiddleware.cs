using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Middleware;

public class ApiKeyAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ObjeXDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }

        var apiKeyHeader = context.Request.Headers["X-API-Key"].FirstOrDefault();

        if (!string.IsNullOrEmpty(apiKeyHeader))
        {
            var apiKey = await db.ApiKeys
                .Include(k => k.User)
                .FirstOrDefaultAsync(k => k.Key == apiKeyHeader);

            if (apiKey is not null && apiKey.ExpiresAt > DateTime.UtcNow)
            {
                apiKey.LastUsedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, apiKey.UserId),
                    new Claim(ClaimTypes.Name, apiKey.User!.UserName!),
                    new Claim(ClaimTypes.Email, apiKey.User.Email!),
                    new Claim("AuthMethod", "ApiKey")
                };

                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
            }
        }

        await next(context);
    }
}
