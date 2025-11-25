using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GameService.ApiService.Models;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    public PlayerProfile? Profile { get; set; }
}

public class PlayerProfile
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    [JsonIgnore]
    public User User { get; set; } = null!;

    public long Coins { get; set; }

    public Dictionary<string, object> Stats { get; set; } = new();

    // FIXED: Use Guid for universal optimistic concurrency
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}

// DTOs
public record struct RegisterRequest(string Username, string Email, string Password);
public record struct LoginRequest(string Username, string Password);
public record struct LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);
public record struct RefreshRequest(string RefreshToken);
public record struct ChangePasswordRequest(string OldPassword, string NewPassword);
public record struct UserResponse(int Id, string Username, string Email);
public record struct PlayerProfileResponse(int UserId, string Username, long Coins, Dictionary<string, object> Stats);
public record struct UpdateCoinRequest(long Amount);