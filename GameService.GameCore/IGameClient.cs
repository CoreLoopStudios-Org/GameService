namespace GameService.GameCore;

public interface IGameClient
{
    Task GameState(GameStateResponse state);

    Task PlayerJoined(PlayerJoinedPayload payload);

    Task PlayerLeft(PlayerLeftPayload payload);

    Task PlayerDisconnected(PlayerDisconnectedPayload payload);

    Task PlayerReconnected(PlayerReconnectedPayload payload);

    Task ActionError(ActionErrorPayload payload);

    Task ChatMessage(ChatMessagePayload payload);

    Task GameEvent(GameEventPayload payload);
}

public sealed record PlayerJoinedPayload(string UserId, string UserName, int SeatIndex);

public sealed record PlayerLeftPayload(string UserId, string UserName);

public sealed record PlayerDisconnectedPayload(string UserId, string UserName, int GracePeriodSeconds);

public sealed record PlayerReconnectedPayload(string UserId, string UserName);

public sealed record ActionErrorPayload(string Action, string ErrorMessage);

public sealed record ChatMessagePayload(string UserId, string UserName, string Message, DateTimeOffset Timestamp);

public sealed record GameEventPayload(string EventName, object Data, DateTimeOffset Timestamp);
