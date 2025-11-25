using System.ComponentModel.DataAnnotations;

namespace GameService.ServiceDefaults.DTOs;

public record struct UpdateCoinRequest(long Amount);

public record struct PlayerProfileResponse(string UserId, long Coins);

public record PlayerUpdatedMessage(string UserId, long NewCoins, string? Username, string? Email);