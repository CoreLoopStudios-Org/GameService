using System.Runtime.CompilerServices;

namespace GameService.GameCore;

public interface IGameRepository<TState> where TState : struct
{
    Task<GameContext<TState>?> LoadAsync(string roomId);
    
    Task<IReadOnlyList<GameContext<TState>>> LoadManyAsync(IReadOnlyList<string> roomIds);
    
    Task SaveAsync(string roomId, TState state, GameRoomMeta meta);
    Task DeleteAsync(string roomId);
    Task<bool> TryAcquireLockAsync(string roomId, TimeSpan timeout);
    Task ReleaseLockAsync(string roomId);
}

public sealed record GameContext<TState>(string RoomId, TState State, GameRoomMeta Meta)
    where TState : struct;

public interface IGameRepositoryFactory
{
    IGameRepository<TState> Create<TState>(string gameType) where TState : struct;
}

public interface IGameState
{
}

public static class GameStateValidator
{
    private const int MaxStateSizeBytes = 1024;

    public static void Validate<T>() where T : struct, IGameState
    {
        var size = Unsafe.SizeOf<T>();
        if (size > MaxStateSizeBytes)
            throw new InvalidOperationException(
                $"Game state {typeof(T).Name} is {size} bytes, exceeding the {MaxStateSizeBytes} byte limit. " +
                "Consider using more compact data structures.");
    }
}