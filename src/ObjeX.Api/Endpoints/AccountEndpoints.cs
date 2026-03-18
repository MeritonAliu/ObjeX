using Microsoft.AspNetCore.Identity;
using ObjeX.Core.Models;

namespace ObjeX.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", async (HttpContext ctx, SignInManager<User> signInManager, ILogger<User> logger) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var login = form["login"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var user = login.Contains('@')
                ? await signInManager.UserManager.FindByEmailAsync(login)
                : await signInManager.UserManager.FindByNameAsync(login);

            if (user is not null)
            {
                var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    if (user.IsDeactivated)
                    {
                        await signInManager.SignOutAsync();
                        var dqs = $"error=1&msg={Uri.EscapeDataString("Your account has been deactivated.")}&login={Uri.EscapeDataString(login)}";
                        if (!string.IsNullOrEmpty(returnUrl)) dqs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                        return Results.Redirect($"/login?{dqs}");
                    }

                    if (user.MustChangePassword)
                    {
                        if (user.TemporaryPasswordExpiresAt.HasValue && user.TemporaryPasswordExpiresAt.Value < DateTime.UtcNow)
                        {
                            await signInManager.SignOutAsync();
                            var eqs = $"error=1&msg={Uri.EscapeDataString("Temporary password expired, contact your administrator.")}&login={Uri.EscapeDataString(login)}";
                            if (!string.IsNullOrEmpty(returnUrl)) eqs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                            return Results.Redirect($"/login?{eqs}");
                        }
                        return Results.Redirect("/change-password");
                    }

                    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
                }
            }

            var sanitizedLogin = login.Replace("\r", "").Replace("\n", "");
            logger.LogWarning("Failed login attempt for {Login} from {IP}", sanitizedLogin, ip);

            var qs = $"error=1&login={Uri.EscapeDataString(login)}";
            if (!string.IsNullOrEmpty(returnUrl)) qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Results.Redirect($"/login?{qs}");
        }).DisableAntiforgery().RequireRateLimiting("login");

        app.MapGet("/account/logout", async (SignInManager<User> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }
}
