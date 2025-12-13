using GameService.GameCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Redis;

public sealed class RedisRoomRegistry(IConnectionMultiplexer redis) : IRoomRegistry
{
    private const string GlobalRegistryKey = "global:room_registry";
    private const string UserRoomKey = "global:user_rooms";
    private const string DisconnectedPlayersKey = "global:disconnected_players";
    private const string OnlineUsersKey = "global:online_users";

    private static readonly TimeSpan ConnectionTtl = TimeSpan.FromMinutes(2);

    private static string UserConnectionsKey(string userId) => $"global:user_connections:{userId}";
    private const string RateLimitKeyPrefix = "ratelimit:";
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<string?> GetGameTypeAsync(string roomId)
    {
        var gameType = await _db.HashGetAsync(GlobalRegistryKey, roomId);
        return gameType.IsNullOrEmpty ? null : gameType.ToString();
    }

    public async Task RegisterRoomAsync(string roomId, string gameType)
    {
        var batch = _db.CreateBatch();
        var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _ = batch.HashSetAsync(GlobalRegistryKey, roomId, gameType);
        _ = batch.SortedSetAddAsync(GameTypeIndexKey(gameType), roomId, score);
        _ = batch.SortedSetAddAsync(ActivityIndexKey(gameType), roomId, score);

        batch.Execute();
        await Task.CompletedTask;
    }

    private const string ShortCodeRegistryKey = "global:short_codes";
    private const string RoomShortCodeKey = "global:room_short_codes";

    public async Task<string> RegisterShortCodeAsync(string roomId)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = string.Create(5, (object?)null, (span, _) =>
            {
                for (var k = 0; k < span.Length; k++)
                    span[k] = (char)('1' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(9));
            });

            if (await _db.HashSetAsync(ShortCodeRegistryKey, code, roomId, When.NotExists))
            {
                await _db.HashSetAsync(RoomShortCodeKey, roomId, code);
                return code;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique short code after multiple attempts.");
    }

    public async Task<string?> GetRoomIdByShortCodeAsync(string shortCode)
    {
        var roomId = await _db.HashGetAsync(ShortCodeRegistryKey, shortCode);
        return roomId.IsNullOrEmpty ? null : roomId.ToString();
    }

    public async Task RemoveShortCodeAsync(string shortCode)
    {
        var roomId = await GetRoomIdByShortCodeAsync(shortCode);
        if (roomId != null)
        {
            var batch = _db.CreateBatch();
            _ = batch.HashDeleteAsync(ShortCodeRegistryKey, shortCode);
            _ = batch.HashDeleteAsync(RoomShortCodeKey, roomId);
            batch.Execute();
        }
    }

    public async Task UnregisterRoomAsync(string roomId)
    {
        var gameType = await GetGameTypeAsync(roomId);
        if (gameType == null) return;

        var batch = _db.CreateBatch();
        _ = batch.HashDeleteAsync(GlobalRegistryKey, roomId);
        _ = batch.SortedSetRemoveAsync(GameTypeIndexKey(gameType), roomId);
        _ = batch.SortedSetRemoveAsync(ActivityIndexKey(gameType), roomId);

        var shortCode = await _db.HashGetAsync(RoomShortCodeKey, roomId);
        if (!shortCode.IsNullOrEmpty)
        {
            _ = batch.HashDeleteAsync(ShortCodeRegistryKey, shortCode);
            _ = batch.HashDeleteAsync(RoomShortCodeKey, roomId);
        }

        batch.Execute();
    }

    public async Task<IReadOnlyList<string>> GetAllRoomIdsAsync()
    {
        var result = new List<string>();
        var cursor = 0L;
        do
        {
            var scanResult = await _db.HashScanAsync(GlobalRegistryKey, "*", 100, cursor).ToArrayAsync();
            foreach (var entry in scanResult)
            {
                result.Add(entry.Name.ToString());
            }
            cursor = scanResult.Length > 0 ? scanResult[^1].Name.GetHashCode() : 0;
            break;
        } while (cursor != 0);

        result.Clear();
        await foreach (var entry in _db.HashScanAsync(GlobalRegistryKey, "*", 100))
        {
            result.Add(entry.Name.ToString());
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> GetRoomIdsByGameTypeAsync(string gameType)
    {
        var members = await _db.SortedSetRangeByRankAsync(GameTypeIndexKey(gameType), 0, 999, Order.Descending);
        return members.Select(m => m.ToString()).ToList();
    }

    public async Task<(IReadOnlyList<string> RoomIds, long NextCursor)> GetRoomIdsPagedAsync(string gameType,
        long cursor = 0, int pageSize = 50)
    {
        var start = cursor;
        var stop = cursor + pageSize - 1;

        var members = await _db.SortedSetRangeByRankAsync(GameTypeIndexKey(gameType), start, stop, Order.Descending);

        var roomIds = members.Select(m => m.ToString()).ToList();

        var nextCursor = roomIds.Count == pageSize ? cursor + pageSize : 0;

        return (roomIds, nextCursor);
    }

    private const string ReleaseLockLua =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    public async Task<bool> TryAcquireLockAsync(string roomId, TimeSpan timeout)
    {
        return await _db.StringSetAsync(LockKey(roomId), Environment.MachineName, timeout, When.NotExists);
    }

    public async Task ReleaseLockAsync(string roomId)
    {
        await _db.ScriptEvaluateAsync(
            ReleaseLockLua,
            [LockKey(roomId)],
            [Environment.MachineName]);
    }

    public async Task SetUserRoomAsync(string userId, string roomId)
    {
        await _db.HashSetAsync(UserRoomKey, userId, roomId);
    }

    public async Task<string?> GetUserRoomAsync(string userId)
    {
        var roomId = await _db.HashGetAsync(UserRoomKey, userId);
        return roomId.IsNullOrEmpty ? null : roomId.ToString();
    }

    public async Task RemoveUserRoomAsync(string userId)
    {
        await _db.HashDeleteAsync(UserRoomKey, userId);
    }

    public async Task SetDisconnectedPlayerAsync(string userId, string roomId, TimeSpan gracePeriod)
    {
        var key = $"{DisconnectedPlayersKey}:{userId}";
        await _db.StringSetAsync(key, roomId, gracePeriod);
    }

    public async Task<string?> TryGetAndRemoveDisconnectedPlayerAsync(string userId)
    {
        var key = $"{DisconnectedPlayersKey}:{userId}";
        var roomId = await _db.StringGetDeleteAsync(key);
        return roomId.IsNullOrEmpty ? null : roomId.ToString();
    }

    public async Task<bool> CheckRateLimitAsync(string userId, int maxPerMinute)
    {
        var key = $"{RateLimitKeyPrefix}{userId}";
        var count = await _db.StringIncrementAsync(key);

        if (count == 1) await _db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));

        return count <= maxPerMinute;
    }

    public async Task<int> IncrementConnectionCountAsync(string userId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var minScore = now - (long)ConnectionTtl.TotalSeconds;
        var key = UserConnectionsKey(userId);

        var batch = _db.CreateBatch();
        var pruneTask = batch.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, minScore);
        var addTask = batch.SortedSetAddAsync(key, connectionId, now);
        var expireTask = batch.KeyExpireAsync(key, ConnectionTtl);
        var onlinePruneTask = batch.SortedSetRemoveRangeByScoreAsync(OnlineUsersKey, double.NegativeInfinity, minScore);
        var onlineAddTask = batch.SortedSetAddAsync(OnlineUsersKey, userId, now);
        var countTask = batch.SortedSetLengthAsync(key);
        batch.Execute();

        await Task.WhenAll(pruneTask, addTask, expireTask, onlinePruneTask, onlineAddTask, countTask);
        return (int)countTask.Result;
    }

    public async Task DecrementConnectionCountAsync(string userId, string connectionId)
    {
        var key = UserConnectionsKey(userId);

        await _db.SortedSetRemoveAsync(key, connectionId);

        var count = await _db.SortedSetLengthAsync(key);
        if (count <= 0)
        {
            await _db.KeyDeleteAsync(key);
            await _db.SortedSetRemoveAsync(OnlineUsersKey, userId);
        }
    }

    public async Task<IReadOnlyList<string>> GetRoomsNeedingTimeoutCheckAsync(string gameType, int maxRooms)
    {
        var maxScore = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds();

        var members = await _db.SortedSetRangeByScoreAsync(
            ActivityIndexKey(gameType),
            double.NegativeInfinity,
            maxScore,
            Exclude.None,
            Order.Ascending,
            0,
            maxRooms);

        return members.Select(m => m.ToString()).ToList();
    }

    public async Task UpdateRoomActivityAsync(string roomId, string gameType)
    {
        var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SortedSetAddAsync(ActivityIndexKey(gameType), roomId, score);
    }

    public async Task<long> GetOnlinePlayerCountAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var minScore = now - (long)ConnectionTtl.TotalSeconds;
        await _db.SortedSetRemoveRangeByScoreAsync(OnlineUsersKey, double.NegativeInfinity, minScore);
        return await _db.SortedSetLengthAsync(OnlineUsersKey);
    }

    public async Task<HashSet<string>> GetOnlineUserIdsAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var minScore = now - (long)ConnectionTtl.TotalSeconds;
        await _db.SortedSetRemoveRangeByScoreAsync(OnlineUsersKey, double.NegativeInfinity, minScore);

        var members = await _db.SortedSetRangeByRankAsync(OnlineUsersKey, 0, -1);
        return members.Select(k => k.ToString()).ToHashSet();
    }

    private static string GameTypeIndexKey(string gameType)
    {
        return $"index:rooms:{gameType}";
    }

    private static string ActivityIndexKey(string gameType)
    {
        return $"index:activity:{gameType}";
    }

    private static string LockKey(string roomId)
    {
        return $"lock:room:{roomId}";
    }
}