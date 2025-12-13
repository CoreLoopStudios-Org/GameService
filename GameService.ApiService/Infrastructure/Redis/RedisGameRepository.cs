using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameService.ApiService;
using GameService.GameCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Redis;

public sealed class StateMigrationRegistry : IStateMigrationRegistry
{
    private readonly Dictionary<(Type, byte, int), object> _migrations = new();

    public void Register<TState>(IStateMigration<TState> migration) where TState : struct
    {
        _migrations[(typeof(TState), migration.FromVersion, migration.FromSize)] = migration;
    }

    public IStateMigration<TState>? GetMigration<TState>(byte fromVersion, int fromSize) where TState : struct
    {
        return _migrations.TryGetValue((typeof(TState), fromVersion, fromSize), out var m)
            ? m as IStateMigration<TState>
            : null;
    }
}

public sealed class RedisGameRepository<TState>(
    IConnectionMultiplexer redis,
    IRoomRegistry roomRegistry,
    string gameType,
    ILogger logger,
    IStateMigrationRegistry? migrationRegistry = null) : IGameRepository<TState>
    where TState : struct
{
    static RedisGameRepository()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<TState>())
            throw new InvalidOperationException($"Type {typeof(TState).Name} must be unmanaged to use RedisGameRepository.");
    }

    private const byte CurrentVersion = 1;

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
            var bytes = (byte[])stateTask.Result!;
            var state = DeserializeStateWithMigration(bytes, roomId);

            var meta = metaTask.Result.IsNullOrEmpty
                ? new GameRoomMeta { GameType = gameType }
                : JsonSerializer.Deserialize(metaTask.Result.ToString(), GameJsonContext.Default.GameRoomMeta) ??
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
        var buffer = ArrayPool<byte>.Shared.Rent(1 + 4 + StateSize);
        try
        {
            var stateBytes = SerializeState(state, buffer);
            var metaJson = JsonSerializer.Serialize(meta, GameJsonContext.Default.GameRoomMeta);

            var batch = _db.CreateBatch();
            var stateTask = batch.StringSetAsync(StateKey(roomId), stateBytes);
            var metaTask = batch.StringSetAsync(MetaKey(roomId), metaJson);
            batch.Execute();

            await Task.WhenAll(stateTask, metaTask);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await roomRegistry.RegisterRoomAsync(roomId, gameType);
    }

    public async Task<IReadOnlyList<GameContext<TState>>> LoadManyAsync(IReadOnlyList<string> roomIds)
    {
        if (roomIds.Count == 0) return [];

        var stateKeys = roomIds.Select(StateKey).ToArray();
        var metaKeys = roomIds.Select(MetaKey).ToArray();

        var stateValues = await _db.StringGetAsync(stateKeys.Select(k => (RedisKey)k).ToArray());
        var metaValues = await _db.StringGetAsync(metaKeys.Select(k => (RedisKey)k).ToArray());

        var results = new List<GameContext<TState>>(roomIds.Count);

        for (var i = 0; i < roomIds.Count; i++)
        {
            if (stateValues[i].IsNullOrEmpty) continue;

            try
            {
                var bytes = (byte[])stateValues[i]!;
                var state = DeserializeStateWithMigration(bytes, roomIds[i]);

                var meta = metaValues[i].IsNullOrEmpty
                    ? new GameRoomMeta { GameType = gameType }
                    : JsonSerializer.Deserialize(metaValues[i].ToString(), GameJsonContext.Default.GameRoomMeta) ??
                      new GameRoomMeta { GameType = gameType };

                results.Add(new GameContext<TState>(roomIds[i], state, meta));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "State corruption detected for room {RoomId} during batch load.", roomIds[i]);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<(string RoomId, GameRoomMeta Meta)>> LoadMetaManyAsync(IReadOnlyList<string> roomIds)
    {
        if (roomIds.Count == 0) return [];

        var metaKeys = roomIds.Select(MetaKey).ToArray();
        var metaValues = await _db.StringGetAsync(metaKeys.Select(k => (RedisKey)k).ToArray());

        var results = new List<(string, GameRoomMeta)>(roomIds.Count);

        for (var i = 0; i < roomIds.Count; i++)
        {
            if (metaValues[i].IsNullOrEmpty) continue;

            var meta = JsonSerializer.Deserialize(metaValues[i].ToString(), GameJsonContext.Default.GameRoomMeta) ??
                       new GameRoomMeta { GameType = gameType };
            results.Add((roomIds[i], meta));
        }

        return results;
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

    private const string ReleaseLockLua =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    public async Task ReleaseLockAsync(string roomId)
    {
        await _db.ScriptEvaluateAsync(
            ReleaseLockLua,
            [LockKey(roomId)],
            [Environment.MachineName]);
    }

    private TState DeserializeStateWithMigration(byte[] bytes, string roomId)
    {
        if (bytes.Length < 5)
            throw new InvalidOperationException("Invalid state data: too short");

        var storedVersion = bytes[0];
        var storedSize = Unsafe.ReadUnaligned<int>(ref bytes[1]);

        if (bytes.Length < 5 + StateSize)
            throw new InvalidOperationException("State corrupted: Buffer too small");

        if (storedVersion == CurrentVersion && storedSize == StateSize)
            return Unsafe.ReadUnaligned<TState>(ref bytes[5]);

        if (migrationRegistry != null)
        {
            var migration = migrationRegistry.GetMigration<TState>(storedVersion, storedSize);
            if (migration != null)
            {
                var oldData = bytes.AsSpan(5, storedSize);
                if (migration.TryMigrate(oldData, out var newState))
                {
                    logger.LogInformation(
                        "Migrated room {RoomId} state from v{FromVersion} ({FromSize}B) to v{ToVersion} ({ToSize}B)",
                        roomId, storedVersion, storedSize, migration.ToVersion, StateSize);
                    return newState;
                }
            }
        }

        throw new InvalidOperationException(
            $"Cannot load state for room {roomId}. " +
            $"Stored: v{storedVersion} ({storedSize}B), Current: v{CurrentVersion} ({StateSize}B). " +
            $"No migration registered. Consider deploying a migration or resetting the room.");
    }

    private string StateKey(string roomId)
    {
        return $"game:{gameType}:{{{roomId}}}:state";
    }

    private string MetaKey(string roomId)
    {
        return $"game:{gameType}:{{{roomId}}}:meta";
    }

    private string LockKey(string roomId)
    {
        return $"game:{gameType}:{{{roomId}}}:lock";
    }

    private static RedisValue SerializeState(TState state, byte[] buffer)
    {
        buffer[0] = CurrentVersion;
        Unsafe.WriteUnaligned(ref buffer[1], StateSize);
        Unsafe.WriteUnaligned(ref buffer[5], state);
        return buffer.AsMemory(0, 1 + 4 + StateSize);
    }
}

public sealed class RedisGameRepositoryFactory(
    IConnectionMultiplexer redis,
    IRoomRegistry roomRegistry,
    ILoggerFactory loggerFactory,
    IStateMigrationRegistry? migrationRegistry = null) : IGameRepositoryFactory
{
    public IGameRepository<TState> Create<TState>(string gameType) where TState : struct
    {
        var logger = loggerFactory.CreateLogger<RedisGameRepository<TState>>();
        return new RedisGameRepository<TState>(redis, roomRegistry, gameType, logger, migrationRegistry);
    }
}