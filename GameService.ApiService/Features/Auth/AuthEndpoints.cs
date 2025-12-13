using System.Security.Claims;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.Security;
using Microsoft.AspNetCore.Mvc;

namespace GameService.ApiService.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");
        group.MapIdentityApi<ApplicationUser>();

        group.MapPost("/logout", async (HttpContext ctx, ITokenRevocationService revocationService) =>
            {
                var jti = ctx.User.FindFirstValue("jti");
                if (!string.IsNullOrEmpty(jti))
                {
                    await revocationService.RevokeTokenAsync(jti);
                }
                else
                {
                    var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        await revocationService.RevokeAccessTokenAsync(token);
                    }
                }

                return Results.Ok(new { Message = "Logged out successfully" });
            })
            .RequireAuthorization()
            .WithName("Logout");
    }
}