namespace GameService.GameCore;

public interface IGameRoomService
{
    Task<string> CreateRoomAsync(string hostUserId);
    Task DeleteRoomAsync(string roomId);
    Task<bool> JoinRoomAsync(string roomId, string userId);
    // We need a way to get generic game state for admin
    Task<object?> GetGameStateAsync(string roomId);
    Task<List<GameRoomDto>> GetActiveGamesAsync();
}

public record GameRoomDto(string RoomId, string GameType, int PlayerCount, bool IsPublic, Dictionary<string, int> PlayerSeats);