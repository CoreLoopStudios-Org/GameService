using System.Security.Cryptography;
using System.Text;
using GameService.ServiceDefaults.Data;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace GameService.ServiceDefaults.Security;

public class Argon2PasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int DefaultDegreeOfParallelism = 1;
    private const int DefaultMemorySize = 65536;
    private const int DefaultIterations = 3;

    public string HashPassword(ApplicationUser user, string password)
    {
        var salt = CreateSalt();
        var hash = HashPasswordInternal(password, salt, DefaultDegreeOfParallelism, DefaultMemorySize,
            DefaultIterations);

        return
            $"$argon2id$v=19$m={DefaultMemorySize},t={DefaultIterations},p={DefaultDegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword,
        string providedPassword)
    {
        try
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 6) return PasswordVerificationResult.Failed;

            var paramsPart = parts[3];
            var paramMap = ParseParameters(paramsPart);

            if (!paramMap.TryGetValue("m", out var memory) ||
                !paramMap.TryGetValue("t", out var iterations) ||
                !paramMap.TryGetValue("p", out var parallelism))
                return PasswordVerificationResult.Failed;

            var salt = Convert.FromBase64String(parts[4]);
            var storedHash = Convert.FromBase64String(parts[5]);

            var newHash = HashPasswordInternal(providedPassword, salt, parallelism, memory, iterations);

            return CryptographicOperations.FixedTimeEquals(storedHash, newHash)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
        catch
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private Dictionary<string, int> ParseParameters(string paramsPart)
    {
        var result = new Dictionary<string, int>();
        var pairs = paramsPart.Split(',');
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[1], out var val)) result[kv[0]] = val;
        }

        return result;
    }

    private byte[] CreateSalt()
    {
        var buffer = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
        return buffer;
    }

    private byte[] HashPasswordInternal(string password, byte[] salt, int parallelism, int memory, int iterations)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            MemorySize = memory,
            Iterations = iterations
        };

        return argon2.GetBytes(32);
    }
}