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
                    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
            }

            logger.LogWarning("Failed login attempt for {Login} from {IP}", login, ip);

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
