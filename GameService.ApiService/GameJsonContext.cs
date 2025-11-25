using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity.Data; // Required for LoginRequest, etc.

namespace GameService.ApiService;

public record struct UpdateCoinRequest(long Amount);
public record struct PlayerProfileResponse(string UserId, long Coins);

[JsonSerializable(typeof(UpdateCoinRequest))]
[JsonSerializable(typeof(PlayerProfileResponse))]
[JsonSerializable(typeof(List<PlayerProfileResponse>))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))] // For validation errors
[JsonSerializable(typeof(Dictionary<string, string[]>))] 
[JsonSerializable(typeof(AccessTokenResponse))] // For Login Response
[JsonSerializable(typeof(LoginRequest))]        // For Login Request
[JsonSerializable(typeof(RegisterRequest))]     // For Register Request
internal partial class GameJsonContext : JsonSerializerContext
{
}