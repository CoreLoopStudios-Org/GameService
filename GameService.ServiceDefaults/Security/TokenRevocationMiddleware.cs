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
            var db = _redis.GetDatabase();
            var jti = context.User.FindFirstValue("jti");
            
            if (!string.IsNullOrEmpty(jti))
            {
                var key = $"{TokenBlacklistPrefix}{jti}";
                
                if (await db.KeyExistsAsync(key))
                {
                    _logger.LogWarning("Revoked token used: jti={Jti}", jti);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Token has been revoked");
                    return;
                }
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var userKey = $"revoked:user:{userId}";
                var revokedAt = await db.StringGetAsync(userKey);
                if (revokedAt.HasValue && long.TryParse(revokedAt.ToString(), out var revokedTime))
                {
                    // If we can't determine token age (no iat), we must assume it's old if user is revoked.
                    // Or we can try to find 'iat' claim.
                    var iatClaim = context.User.FindFirstValue("iat");
                    if (long.TryParse(iatClaim, out var iat))
                    {
                        if (iat < revokedTime)
                        {
                            _logger.LogWarning("Token issued before user revocation used: user={UserId}", userId);
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await context.Response.WriteAsync("Session expired");
                            return;
                        }
                    }
                    else
                    {
                        // Fallback: if no iat, and user is revoked, block.
                        // This is aggressive but safe for "Logout" scenario where we want to kill sessions.
                        _logger.LogWarning("Token without iat used for revoked user: user={UserId}", userId);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Session expired");
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

    Task RevokeAllUserTokensAsync(string userId);

    Task<bool> IsTokenRevokedAsync(string jti);
}

public sealed class RedisTokenRevocationService : ITokenRevocationService
{
    private const string TokenBlacklistPrefix = "revoked:jti:";
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
}

public static class TokenRevocationExtensions
{
    public static IApplicationBuilder UseTokenRevocation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TokenRevocationMiddleware>();
    }
}
