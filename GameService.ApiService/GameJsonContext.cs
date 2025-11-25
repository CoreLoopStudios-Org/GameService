using System.Text.Json.Serialization;
using GameService.ServiceDefaults.Messages;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity.Data;
using GameService.ServiceDefaults.Messages;


namespace GameService.ApiService;

public record struct UpdateCoinRequest(long Amount);
public record struct PlayerProfileResponse(string UserId, long Coins);

[JsonSerializable(typeof(UpdateCoinRequest))]
[JsonSerializable(typeof(PlayerProfileResponse))]
[JsonSerializable(typeof(List<PlayerProfileResponse>))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, string[]>))] 
[JsonSerializable(typeof(AccessTokenResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(PlayerUpdatedMessage))]
internal partial class GameJsonContext : JsonSerializerContext
{
}



