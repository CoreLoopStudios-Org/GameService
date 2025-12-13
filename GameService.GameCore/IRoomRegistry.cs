namespace GameService.GameCore;

public interface IRoomRegistry
{
    Task<string?> GetGameTypeAsync(string roomId);

    Task RegisterRoomAsync(string roomId, string gameType);

    Task UnregisterRoomAsync(string roomId);

    Task<IReadOnlyList<string>> GetAllRoomIdsAsync();

    Task<IReadOnlyList<string>> GetRoomIdsByGameTypeAsync(string gameType);

    Task<(IReadOnlyList<string> RoomIds, long NextCursor)> GetRoomIdsPagedAsync(string gameType, long cursor = 0,
        int pageSize = 50);

    Task<bool> TryAcquireLockAsync(string roomId, TimeSpan timeout);

    Task ReleaseLockAsync(string roomId);

    Task SetUserRoomAsync(string userId, string roomId);

    Task<string?> GetUserRoomAsync(string userId);

    Task RemoveUserRoomAsync(string userId);

    Task SetDisconnectedPlayerAsync(string userId, string roomId, TimeSpan gracePeriod);

    Task<string?> TryGetAndRemoveDisconnectedPlayerAsync(string userId);

    Task<bool> CheckRateLimitAsync(string userId, int maxPerMinute);

    Task<int> IncrementConnectionCountAsync(string userId, string connectionId);

    Task DecrementConnectionCountAsync(string userId, string connectionId);

    Task<IReadOnlyList<string>> GetRoomsNeedingTimeoutCheckAsync(string gameType, int maxRooms);

    Task UpdateRoomActivityAsync(string roomId, string gameType);

    Task<long> GetOnlinePlayerCountAsync();

    Task<HashSet<string>> GetOnlineUserIdsAsync();

    Task<string> RegisterShortCodeAsync(string roomId);

    Task<string?> GetRoomIdByShortCodeAsync(string shortCode);

    Task RemoveShortCodeAsync(string shortCode);
}