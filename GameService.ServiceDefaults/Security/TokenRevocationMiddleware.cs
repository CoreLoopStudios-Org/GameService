using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Claims;

namespace GameService.ServiceDefaults.Security;

public sealed class TokenRevocationMiddleware
{
    private const string TokenBlacklistPrefix = "revoked:jti:";
    private readonly ILogger<TokenRevocationMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;

    public TokenRevocationMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _redis = redis;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirstValue("jti");
            var revocationService = context.RequestServices.GetService(typeof(ITokenRevocationService)) as ITokenRevocationService;

            if (revocationService != null)
            {
                if (!string.IsNullOrEmpty(jti))
                {
                    if (await revocationService.IsTokenRevokedAsync(jti))
                    {
                        _logger.LogWarning("Revoked token used: jti={Jti}", jti);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Token has been revoked");
                        return;
                    }
                }
                else
                {
                    // Fallback: Check if the specific access token string is revoked
                    var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(token) && await revocationService.IsAccessTokenRevokedAsync(token))
                    {
                        _logger.LogWarning("Revoked access token used (no jti)");
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Token has been revoked");
                        return;
                    }
                }
            }
        }

        await _next(context);
    }
}

public interface ITokenRevocationService
{
    Task RevokeTokenAsync(string jti, TimeSpan? ttl = null);

    Task RevokeAccessTokenAsync(string token, TimeSpan? ttl = null);

    Task RevokeAllUserTokensAsync(string userId);

    Task<bool> IsTokenRevokedAsync(string jti);

    Task<bool> IsAccessTokenRevokedAsync(string token);
}

public sealed class RedisTokenRevocationService : ITokenRevocationService
{
    private const string TokenBlacklistPrefix = "revoked:jti:";
    private const string AccessTokenBlacklistPrefix = "revoked:token:";
    private const string UserRevocationPrefix = "revoked:user:";
    private readonly IDatabase _db;
    private readonly ILogger<RedisTokenRevocationService> _logger;

    public RedisTokenRevocationService(
        IConnectionMultiplexer redis,
        ILogger<RedisTokenRevocationService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task RevokeTokenAsync(string jti, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(jti)) return;

        var key = $"{TokenBlacklistPrefix}{jti}";
        var expiry = ttl ?? TimeSpan.FromHours(24);
        
        await _db.StringSetAsync(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), expiry);
        _logger.LogInformation("Token revoked: jti={Jti}, expires in {Expiry}", jti, expiry);
    }

    public async Task RevokeAccessTokenAsync(string token, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(token)) return;

        var hash = ComputeTokenHash(token);
        var key = $"{AccessTokenBlacklistPrefix}{hash}";
        var expiry = ttl ?? TimeSpan.FromHours(24);

        await _db.StringSetAsync(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), expiry);
        _logger.LogInformation("Access token revoked (hash={Hash}), expires in {Expiry}", hash, expiry);
    }

    public async Task RevokeAllUserTokensAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        var key = $"{UserRevocationPrefix}{userId}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _db.StringSetAsync(key, timestamp.ToString(), TimeSpan.FromDays(7));
        _logger.LogInformation("All tokens revoked for user {UserId} at {Timestamp}", userId, timestamp);
    }

    public async Task<bool> IsTokenRevokedAsync(string jti)
    {
        if (string.IsNullOrEmpty(jti)) return false;

        var key = $"{TokenBlacklistPrefix}{jti}";
        return await _db.KeyExistsAsync(key);
    }

    public async Task<bool> IsAccessTokenRevokedAsync(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        var hash = ComputeTokenHash(token);
        var key = $"{AccessTokenBlacklistPrefix}{hash}";
        return await _db.KeyExistsAsync(key);
    }

    private static string ComputeTokenHash(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

public static class TokenRevocationExtensions
{
    public static IApplicationBuilder UseTokenRevocation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TokenRevocationMiddleware>();
    }
}
