using System.ComponentModel.DataAnnotations;

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
}

// DTOs
public record struct RegisterRequest(string Username, string Email, string Password);
public record struct LoginRequest(string Username, string Password);
public record struct LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);
public record struct RefreshRequest(string RefreshToken);
public record struct ChangePasswordRequest(string OldPassword, string NewPassword);
public record struct UserResponse(int Id, string Username, string Email);