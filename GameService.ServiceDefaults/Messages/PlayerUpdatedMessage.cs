namespace GameService.ServiceDefaults.Messages;
public record PlayerUpdatedMessage(string UserId, long NewCoins, string? Username, string? Email);