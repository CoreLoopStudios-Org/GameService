using System.Runtime.CompilerServices;
using System.Text.Json;
using GameService.GameCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Redis;

/// <summary>
/// Migration strategy interface for handling struct layout changes between deployments.
/// Implement per-game-type migrations when struct fields change.
/// </summary>
public interface IStateMigration<TState> where TState : struct
{
    /// <summary>
    /// Source version this migration handles (migrates FROM this version)
    /// </summary>
    byte FromVersion { get; }
    
    /// <summary>
    /// Target version this migration produces (migrates TO this version)
    /// </summary>
    byte ToVersion { get; }
    
    /// <summary>
    /// Expected size of the source struct in bytes
    /// </summary>
    int FromSize { get; }
    
    /// <summary>
    /// Migrate from old bytes to new state. Returns true if migration succeeded.
    /// </summary>
    bool TryMigrate(ReadOnlySpan<byte> oldData, out TState newState);
}

/// <summary>
/// Registry for state migrations. Games register migrations at startup.
/// </summary>
public interface IStateMigrationRegistry
{
    void Register<TState>(IStateMigration<TState> migration) where TState : struct;
    IStateMigration<TState>? GetMigration<TState>(byte fromVersion, int fromSize) where TState : struct;
}

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
    
    private TState DeserializeStateWithMigration(byte[] bytes, string roomId)
    {
        if (bytes.Length < 5) 
            throw new InvalidOperationException("Invalid state data: too short");

        var storedVersion = bytes[0];
        var storedSize = Unsafe.ReadUnaligned<int>(ref bytes[1]);
        
        // Fast path: version and size match
        if (storedVersion == CurrentVersion && storedSize == StateSize)
        {
            return Unsafe.ReadUnaligned<TState>(ref bytes[5]);
        }
        
        // Try to migrate from old version/size
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
        
        // No migration available - log detailed error for debugging
        throw new InvalidOperationException(
            $"Cannot load state for room {roomId}. " +
            $"Stored: v{storedVersion} ({storedSize}B), Current: v{CurrentVersion} ({StateSize}B). " +
            $"No migration registered. Consider deploying a migration or resetting the room.");
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
        bytes[0] = CurrentVersion;
        Unsafe.WriteUnaligned(ref bytes[1], StateSize);
        Unsafe.WriteUnaligned(ref bytes[5], state);
        return bytes;
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