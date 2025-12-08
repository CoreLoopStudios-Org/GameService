using System.Security.Cryptography;
using GameService.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameService.ServiceDefaults.Security;

public sealed class SecurityValidator
{
    private readonly AdminSettings _adminSettings;
    private readonly IHostEnvironment _environment;
    private readonly GameServiceOptions _gameOptions;
    private readonly ILogger<SecurityValidator> _logger;

    public SecurityValidator(
        ILogger<SecurityValidator> logger,
        IHostEnvironment environment,
        IOptions<GameServiceOptions> gameOptions,
        IOptions<AdminSettings> adminSettings)
    {
        _logger = logger;
        _environment = environment;
        _gameOptions = gameOptions.Value;
        _adminSettings = adminSettings.Value;
    }

    public void Validate()
    {
        var issues = new List<string>();
        var warnings = new List<string>();

        ValidateApiKey(issues, warnings);
        ValidateAdminSeed(issues, warnings);
        ValidatePasswordPolicy(warnings);

        foreach (var warning in warnings) _logger.LogWarning("⚠️ Security Warning: {Warning}", warning);

        if (issues.Count > 0)
        {
            foreach (var issue in issues) _logger.LogCritical("🚨 Security Issue: {Issue}", issue);

            if (!_environment.IsDevelopment())
                throw new InvalidOperationException(
                    $"Security validation failed with {issues.Count} critical issue(s). " +
                    $"Fix these before deploying to production:\n• " + string.Join("\n• ", issues));

            _logger.LogWarning("🔧 Running in Development - security issues are warnings only. " +
                               "These WILL block startup in production.");
        }
        else
        {
            _logger.LogInformation("✅ Security validation passed");
        }
    }

    private void ValidateApiKey(List<string> issues, List<string> warnings)
    {
        var apiKey = _adminSettings.ApiKey;
        var minLength = _gameOptions.Security.MinimumApiKeyLength;

        if (string.IsNullOrEmpty(apiKey))
        {
            if (!_environment.IsDevelopment())
                issues.Add(
                    "AdminSettings:ApiKey is not configured. Set via environment variable: AdminSettings__ApiKey");
            else
                warnings.Add("AdminSettings:ApiKey is not set. Admin endpoints will be inaccessible.");
            return;
        }

        var weakKeys = new[]
        {
            "DevOnlyAdminKey-ChangeInProduction!",
            "admin",
            "password",
            "secret",
            "apikey",
            "test",
            "12345678"
        };

        if (weakKeys.Any(weak => apiKey.Contains(weak, StringComparison.OrdinalIgnoreCase)))
            issues.Add("AdminSettings:ApiKey contains a weak/default value. Generate a strong random key.");

        if (apiKey.Length < minLength)
        {
            if (_gameOptions.Security.EnforceApiKeyValidation)
                issues.Add($"AdminSettings:ApiKey must be at least {minLength} characters. Current: {apiKey.Length}");
            else
                warnings.Add($"AdminSettings:ApiKey is shorter than recommended ({apiKey.Length} < {minLength})");
        }

        if (!HasSufficientEntropy(apiKey))
            warnings.Add("AdminSettings:ApiKey has low entropy. Consider using a cryptographically random value.");
    }

    private void ValidateAdminSeed(List<string> issues, List<string> warnings)
    {
        var email = _gameOptions.AdminSeed.Email;
        var password = _gameOptions.AdminSeed.Password;

        if (!_environment.IsDevelopment())
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _logger.LogInformation("Admin seed credentials not configured. " +
                                       "Create admin account via secure channel.");
                return;
            }

        var weakPasswords = new[]
        {
            "AdminPass123!",
            "password",
            "admin",
            "123456",
            "Password1!"
        };

        if (weakPasswords.Any(weak => password.Equals(weak, StringComparison.OrdinalIgnoreCase)))
        {
            if (!_environment.IsDevelopment())
                issues.Add("GameService:AdminSeed:Password uses a weak/default value. Generate a strong password.");
            else
                warnings.Add("Admin seed password is weak. Change before production.");
        }
    }

    private void ValidatePasswordPolicy(List<string> warnings)
    {
        warnings.Add("Consider enabling 2FA for admin accounts in production.");
    }

    private static bool HasSufficientEntropy(string value)
    {
        if (value.Length < 16) return false;

        var hasLower = value.Any(char.IsLower);
        var hasUpper = value.Any(char.IsUpper);
        var hasDigit = value.Any(char.IsDigit);
        var hasSpecial = value.Any(c => !char.IsLetterOrDigit(c));

        return hasLower && hasUpper && hasDigit && hasSpecial;
    }

    public static string GenerateSecureApiKey(int length = 64)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];

        for (var i = 0; i < length; i++) result[i] = chars[bytes[i] % chars.Length];

        return new string(result);
    }
}

public static class SecurityValidatorExtensions
{
    public static IServiceCollection AddSecurityValidation(this IServiceCollection services)
    {
        services.AddSingleton<SecurityValidator>();
        return services;
    }

    public static WebApplication ValidateSecurity(this WebApplication app)
    {
        var validator = app.Services.GetRequiredService<SecurityValidator>();
        validator.Validate();
        return app;
    }
}