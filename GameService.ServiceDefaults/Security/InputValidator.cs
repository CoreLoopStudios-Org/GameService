using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameService.ServiceDefaults.Security;

public static partial class InputValidator
{
    private const int MaxEmailLength = 254;
    private const int MaxUsernameLength = 100;
    private const int MaxReferenceIdLength = 100;
    private const int MaxIdempotencyKeyLength = 64;
    private const int MaxRoomIdLength = 50;
    private const int MaxGameTypeLength = 50;
    private const int MaxTemplateNameLength = 100;
    private const int MaxConfigJsonLength = 4096;

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Length > MaxEmailLength) return false;

        return new EmailAddressAttribute().IsValid(email);
    }

    public static bool IsValidUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        return Guid.TryParse(userId, out _);
    }

    public static bool IsValidRoomId(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return false;
        if (roomId.Length > MaxRoomIdLength) return false;

        return HexPattern().IsMatch(roomId);
    }

    public static bool IsValidGameType(string? gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType)) return false;
        if (gameType.Length > MaxGameTypeLength) return false;

        return AlphanumericPattern().IsMatch(gameType);
    }

    public static bool IsValidTemplateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > MaxTemplateNameLength) return false;

        return SafeNamePattern().IsMatch(name);
    }

    public static bool IsValidReferenceId(string? referenceId)
    {
        if (string.IsNullOrEmpty(referenceId)) return true;
        if (referenceId.Length > MaxReferenceIdLength) return false;

        return ReferenceIdPattern().IsMatch(referenceId);
    }

    public static bool IsValidIdempotencyKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (key.Length > MaxIdempotencyKeyLength) return false;

        return IdempotencyKeyPattern().IsMatch(key);
    }

    public static bool IsValidCoinAmount(long amount)
    {
        const long maxAmount = 1_000_000_000_000;
        return amount is >= -maxAmount and <= maxAmount;
    }

    public static bool IsValidConfigJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return true;
        if (json.Length > MaxConfigJsonLength) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizeForLogging(string? input, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(input)) return "[empty]";

        var sanitized = input.Length > maxLength
            ? input[..maxLength] + "..."
            : input;

        return LogSafePattern().Replace(sanitized, "_");
    }

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexPattern();

    [GeneratedRegex("^[a-zA-Z0-9]+$")]
    private static partial Regex AlphanumericPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_(),.]+$")]
    private static partial Regex SafeNamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_:\-]+$")]
    private static partial Regex ReferenceIdPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]+$")]
    private static partial Regex IdempotencyKeyPattern();

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex LogSafePattern();
}

public readonly record struct ValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static ValidationResult Success => new(true);

    public static ValidationResult Error(string message)
    {
        return new ValidationResult(false, message);
    }
}