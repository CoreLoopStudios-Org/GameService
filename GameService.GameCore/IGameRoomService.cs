namespace GameService.GameCore;

public interface IGameRoomService
{
    string GameType { get; }

    Task<string> CreateRoomAsync(GameRoomMeta config);

    Task DeleteRoomAsync(string roomId);
    Task<JoinRoomResult> JoinRoomAsync(string roomId, string userId);
    Task LeaveRoomAsync(string roomId, string userId);
    Task<GameRoomMeta?> GetRoomMetaAsync(string roomId);
}

public sealed record JoinRoomResult(bool Success, string? ErrorMessage = null, int SeatIndex = -1)
{
    public static JoinRoomResult Ok(int seatIndex)
    {
        return new JoinRoomResult(true, null, seatIndex);
    }

    public static JoinRoomResult Error(string message)
    {
        return new JoinRoomResult(false, message);
    }
}

public sealed record GameRoomDto(
    string RoomId,
    string GameType,
    int PlayerCount,
    int MaxPlayers,
    bool IsPublic,
    IReadOnlyDictionary<string, int> PlayerSeats);