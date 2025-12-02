using System.Runtime.CompilerServices;
using System.Text.Json;
using GameService.GameCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Redis;

public sealed class RedisGameRepository<TState>(
    IConnectionMultiplexer redis,
    IRoomRegistry roomRegistry,
    string gameType,
    ILogger logger) : IGameRepository<TState>
    where TState : struct
{
    private const byte VersionHeader = 1;

    private static readonly int StateSize = Unsafe.SizeOf<TState>();
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<GameContext<TState>?> LoadAsync(string roomId)
    {
        var batch = _db.CreateBatch();
        var stateTask = batch.StringGetAsync(StateKey(roomId));
        var metaTask = batch.StringGetAsync(MetaKey(roomId));
        batch.Execute();

        await Task.WhenAll(stateTask, metaTask);

        if (stateTask.Result.IsNullOrEmpty) return null;

        try
        {
            var state = DeserializeState((byte[])stateTask.Result!);

            var meta = metaTask.Result.IsNullOrEmpty
                ? new GameRoomMeta { GameType = gameType }
                : JsonSerializer.Deserialize<GameRoomMeta>(metaTask.Result.ToString()) ??
                  new GameRoomMeta { GameType = gameType };

            return new GameContext<TState>(roomId, state, meta);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "State corruption detected for room {RoomId}. Resetting state.", roomId);
            return null;
        }
    }

    public async Task SaveAsync(string roomId, TState state, GameRoomMeta meta)
    {
        var stateBytes = SerializeState(state);
        var metaJson = JsonSerializer.Serialize(meta);

        var batch = _db.CreateBatch();
        _ = batch.StringSetAsync(StateKey(roomId), stateBytes);
        _ = batch.StringSetAsync(MetaKey(roomId), metaJson);
        batch.Execute();

        await roomRegistry.RegisterRoomAsync(roomId, gameType);
    }

    public async Task DeleteAsync(string roomId)
    {
        await _db.KeyDeleteAsync([StateKey(roomId), MetaKey(roomId), LockKey(roomId)]);
        await roomRegistry.UnregisterRoomAsync(roomId);
    }

    public async Task<bool> TryAcquireLockAsync(string roomId, TimeSpan timeout)
    {
        return await _db.StringSetAsync(LockKey(roomId), Environment.MachineName, timeout, When.NotExists);
    }

    public async Task ReleaseLockAsync(string roomId)
    {
        await _db.KeyDeleteAsync(LockKey(roomId));
    }

    private string StateKey(string roomId)
    {
        return $"{{game:{gameType}}}:{roomId}:state";
    }

    private string MetaKey(string roomId)
    {
        return $"{{game:{gameType}}}:{roomId}:meta";
    }

    private string LockKey(string roomId)
    {
        return $"{{game:{gameType}}}:{roomId}:lock";
    }

    private static byte[] SerializeState(TState state)
    {
        var bytes = new byte[1 + 4 + StateSize];
        bytes[0] = VersionHeader;
        Unsafe.WriteUnaligned(ref bytes[1], StateSize);
        Unsafe.WriteUnaligned(ref bytes[5], state);
        return bytes;
    }

    private static TState DeserializeState(byte[] bytes)
    {
        if (bytes.Length < 5) throw new InvalidOperationException("Invalid state data: too short");

        if (bytes[0] != VersionHeader)
            throw new InvalidOperationException($"Version mismatch. Expected {VersionHeader}, got {bytes[0]}");

        var storedSize = Unsafe.ReadUnaligned<int>(ref bytes[1]);
        if (storedSize != StateSize)
            throw new InvalidOperationException(
                $"Struct size mismatch. Expected {StateSize}, got {storedSize}. Deployment changed data layout.");

        return Unsafe.ReadUnaligned<TState>(ref bytes[5]);
    }
}

public sealed class RedisGameRepositoryFactory(
    IConnectionMultiplexer redis,
    IRoomRegistry roomRegistry,
    ILoggerFactory loggerFactory) : IGameRepositoryFactory
{
    public IGameRepository<TState> Create<TState>(string gameType) where TState : struct
    {
        var logger = loggerFactory.CreateLogger<RedisGameRepository<TState>>();
        return new RedisGameRepository<TState>(redis, roomRegistry, gameType, logger);
    }
}