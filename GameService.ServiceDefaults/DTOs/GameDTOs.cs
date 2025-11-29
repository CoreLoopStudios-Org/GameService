using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GameService.ServiceDefaults.DTOs;

public record struct UpdateCoinRequest(long Amount);

public record struct PlayerProfileResponse(string UserId, long Coins);

public record AdminPlayerDto(int ProfileId, string UserId, string Username, string Email, long Coins);

public enum PlayerChangeType { Updated, Deleted }

public record PlayerUpdatedMessage(
    string UserId, 
    long NewCoins, 
    string? Username, 
    string? Email,
    PlayerChangeType ChangeType = PlayerChangeType.Updated,
    int ProfileId = 0);

public record SupportedGameDto(string Name);