using GameService.GameCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Redis;

public sealed class RedisRoomRegistry(IConnectionMultiplexer redis, ILogger<RedisRoomRegistry> logger) : IRoomRegistry
{
    private readonly IDatabase _db = redis.GetDatabase();

    // Mapping: RoomId -> GameType
    private const string GlobalRegistryKey = "global:room_registry";
    
    // Index: GameType -> RoomId (Sorted by creation time/score)
    private static string GameTypeIndexKey(string gameType) => $"index:rooms:{gameType}";
    
    private static string LockKey(string roomId) => $"lock:room:{roomId}";

    public async Task<string?> GetGameTypeAsync(string roomId)
    {
        var gameType = await _db.HashGetAsync(GlobalRegistryKey, roomId);
        return gameType.IsNullOrEmpty ? null : gameType.ToString();
    }

    public async Task RegisterRoomAsync(string roomId, string gameType)
    {
        var batch = _db.CreateBatch();
        var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Map ID to Type
        _ = batch.HashSetAsync(GlobalRegistryKey, roomId, gameType);
        
        // 2. Add to Sorted Set Index (O(log(N)))
        _ = batch.SortedSetAddAsync(GameTypeIndexKey(gameType), roomId, score);
        
        batch.Execute();
        await Task.CompletedTask; // Fire and forget the batch
    }

    public async Task UnregisterRoomAsync(string roomId)
    {
        var gameType = await GetGameTypeAsync(roomId);
        if (gameType == null) return;

        var batch = _db.CreateBatch();
        _ = batch.HashDeleteAsync(GlobalRegistryKey, roomId);
        _ = batch.SortedSetRemoveAsync(GameTypeIndexKey(gameType), roomId);
        batch.Execute();
    }

    public async Task<IReadOnlyList<string>> GetAllRoomIdsAsync()
    {
        // Warning: This is still heavy, but better than SCAN. Use carefully.
        var entries = await _db.HashKeysAsync(GlobalRegistryKey);
        return entries.Select(e => e.ToString()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetRoomIdsByGameTypeAsync(string gameType)
    {
        // Limit to last 1000 to prevent blowing up memory
        var members = await _db.SortedSetRangeByRankAsync(GameTypeIndexKey(gameType), 0, 999, Order.Descending);
        return members.Select(m => m.ToString()).ToList();
    }

    public async Task<(IReadOnlyList<string> RoomIds, long NextCursor)> GetRoomIdsPagedAsync(string gameType, long cursor = 0, int pageSize = 50)
    {
        // Efficient O(log(N) + M) pagination using Sorted Sets
        // Cursor here acts as the "Skip" count (Rank)
        var start = cursor;
        var stop = cursor + pageSize - 1;

        var members = await _db.SortedSetRangeByRankAsync(GameTypeIndexKey(gameType), start, stop, Order.Descending);
        
        var roomIds = members.Select(m => m.ToString()).ToList();
        
        // If we got a full page, the next cursor is start + count. Otherwise 0 (end).
        var nextCursor = roomIds.Count == pageSize ? cursor + pageSize : 0;
        
        return (roomIds, nextCursor);
    }

    public async Task<bool> TryAcquireLockAsync(string roomId, TimeSpan timeout)
    {
        return await _db.StringSetAsync(LockKey(roomId), Environment.MachineName, timeout, When.NotExists);
    }

    public async Task ReleaseLockAsync(string roomId)
    {
        await _db.KeyDeleteAsync(LockKey(roomId));
    }
}