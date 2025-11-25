namespace GameService.ApiService.DTOs;

public record struct RegisterRequest(string Username, string Email, string Password);
public record struct LoginRequest(string Username, string Password);
public record struct LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);
public record struct RefreshRequest(string RefreshToken);
public record struct ChangePasswordRequest(string OldPassword, string NewPassword);
public record struct UserResponse(int Id, string Username, string Email);
public record struct PlayerProfileResponse(int UserId, string Username, long Coins);
public record struct UpdateCoinRequest(long Amount);