using System.Text.Json.Serialization;
using GameService.ApiService.DTOs;

namespace GameService.ApiService;

[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(List<UserResponse>))]
[JsonSerializable(typeof(PlayerProfileResponse))]
[JsonSerializable(typeof(UpdateCoinRequest))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
internal partial class GameJsonContext : JsonSerializerContext
{
}