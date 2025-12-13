using System.Text.Json;

namespace GameService.GameCore;

public interface IGameEngine
{
    string GameType { get; }

    Task<GameActionResult> ExecuteAsync(string roomId, GameCommand command);

    Task<IReadOnlyList<string>> GetLegalActionsAsync(string roomId, string userId);

    Task<GameStateResponse?> GetStateAsync(string roomId);
    
    Task<IReadOnlyList<GameStateResponse>> GetManyStatesAsync(IReadOnlyList<string> roomIds);

    Task<IReadOnlyList<(string RoomId, GameRoomMeta Meta)>> GetManyMetasAsync(IReadOnlyList<string> roomIds);
}

public interface ITurnBasedGameEngine : IGameEngine
{
    int TurnTimeoutSeconds { get; }

    Task<GameActionResult?> CheckTimeoutsAsync(string roomId);
}

public sealed record GameCommand(
    string UserId,
    string Action,
    JsonElement Payload)
{
    public T? GetPayload<T>() where T : class
    {
        if (Payload.ValueKind == JsonValueKind.Undefined || Payload.ValueKind == JsonValueKind.Null)
            return null;
        return Payload.Deserialize<T>();
    }

    public int GetInt(string propertyName, int defaultValue = 0)
    {
        if (Payload.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out var value))
            return value;
        return defaultValue;
    }
}

public sealed record GameActionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ShouldBroadcast { get; init; }
    public object? NewState { get; init; }
    public IReadOnlyList<GameEvent> Events { get; init; } = [];

    public GameEndedInfo? GameEnded { get; init; }

    public static GameActionResult Error(string message)
    {
        return new GameActionResult { Success = false, ErrorMessage = message, ShouldBroadcast = false, Events = [] };
    }

    public static GameActionResult Ok(object? state = null, params GameEvent[] events)
    {
        return new GameActionResult { Success = true, ShouldBroadcast = true, NewState = state, Events = events };
    }

    public static GameActionResult OkNoState(params GameEvent[] events)
    {
        return new GameActionResult { Success = true, ShouldBroadcast = false, Events = events };
    }

    public static GameActionResult GameOver(
        object? state,
        GameEndedInfo gameEndedInfo,
        params GameEvent[] events)
    {
        return new GameActionResult
        {
            Success = true,
            ShouldBroadcast = true,
            NewState = state,
            Events = events,
            GameEnded = gameEndedInfo
        };
    }
}

public sealed record GameEndedInfo(
    string RoomId,
    string GameType,
    IReadOnlyDictionary<string, int> PlayerSeats,
    string? WinnerUserId,
    long TotalPot,
    DateTimeOffset StartedAt,
    IReadOnlyList<string>? WinnerRanking = null);

public sealed record GameEvent(string EventName, object Data)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GameStateResponse
{
    public required string RoomId { get; init; }
    public required string GameType { get; init; }
    public required GameRoomMeta Meta { get; init; }
    public required object State { get; init; }
    public IReadOnlyList<string> LegalMoves { get; init; } = [];
}

public sealed record GameRoomMeta
{
    public Dictionary<string, int> PlayerSeats { get; init; } = new();
    public bool IsPublic { get; init; } = true;
    public string GameType { get; init; } = "";
    public int MaxPlayers { get; init; } = 4;

    public long EntryFee { get; init; } = 0;
    public Dictionary<string, string> Config { get; init; } = new();

    public int CurrentPlayerCount => PlayerSeats.Count;

    public DateTimeOffset TurnStartedAt { get; init; } = DateTimeOffset.UtcNow;

    public Dictionary<string, DateTimeOffset> DisconnectedPlayers { get; init; } = new();
}