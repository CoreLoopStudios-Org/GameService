using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GameService.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameService.ServiceDefaults.Security;

/// <summary>
///     Middleware for API key authentication with production-grade security.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly AdminSettings _adminSettings;
    private readonly byte[] _configuredKeyBytes;
    private readonly IHostEnvironment _environment;
    private readonly GameServiceOptions _gameOptions;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly RequestDelegate _next;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IOptions<AdminSettings> adminSettings,
        IOptions<GameServiceOptions> gameOptions,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _adminSettings = adminSettings.Value;
        _gameOptions = gameOptions.Value;
        _environment = environment;

        _configuredKeyBytes = string.IsNullOrEmpty(_adminSettings.ApiKey)
            ? []
            : Encoding.UTF8.GetBytes(_adminSettings.ApiKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();

        if (!string.IsNullOrEmpty(apiKey))
        {
            if (_configuredKeyBytes.Length == 0)
            {
                if (!_environment.IsDevelopment())
                    _logger.LogWarning("API key authentication attempted but no key is configured");
                await _next(context);
                return;
            }

            var providedKeyBytes = Encoding.UTF8.GetBytes(apiKey);

            if (SecureCompare(providedKeyBytes, _configuredKeyBytes))
            {
                if (!_environment.IsDevelopment() &&
                    _gameOptions.Security.EnforceApiKeyValidation &&
                    _adminSettings.ApiKey.Length < _gameOptions.Security.MinimumApiKeyLength)
                {
                    _logger.LogError("API key is too short for production use. Rejecting request.");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("Service misconfigured. Contact administrator.");
                    return;
                }

                var claims = new[]
                {
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(ClaimTypes.NameIdentifier, "api-key-admin"),
                    new Claim(ClaimTypes.AuthenticationMethod, "ApiKey"),
                    new Claim("api_key_auth", "true")
                };
                var identity = new ClaimsIdentity(claims, "ApiKey");
                context.User = new ClaimsPrincipal(identity);

                _logger.LogDebug("API key authentication successful for {Path}", context.Request.Path);
            }
            else
            {
                _logger.LogWarning("Invalid API key attempt from {IP} for {Path}",
                    context.Connection.RemoteIpAddress,
                    context.Request.Path);
            }
        }

        await _next(context);
    }

    /// <summary>
    ///     Constant-time comparison to prevent timing attacks
    /// </summary>
    private static bool SecureCompare(byte[] _providedKeyBytes, byte[] configuredKeyBytes)
    {
        if (_providedKeyBytes.Length != configuredKeyBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(_providedKeyBytes, _providedKeyBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_providedKeyBytes, configuredKeyBytes);
    }
}

/// <summary>
///     Extension methods for API key middleware
/// </summary>
public static class ApiKeyAuthenticationExtensions
{
    /// <summary>
    ///     Adds API key authentication middleware.
    ///     Must be called after UseAuthentication() and before UseAuthorization()
    /// </summary>
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
    }
}