using System.Text.Json;
using GameService.ApiService.Hubs;
using GameService.GameCore;
using GameService.ServiceDefaults.Configuration;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Workers;

public sealed class GameLoopWorker(
    IServiceProvider serviceProvider,
    IRoomRegistry roomRegistry,
    IGameBroadcaster broadcaster,
    IOptions<GameServiceOptions> options,
    IConnectionMultiplexer redis,
    ILogger<GameLoopWorker> logger) : BackgroundService
{
    private const int MaxRoomsPerTick = 50;
    private readonly int _tickIntervalMs = options.Value.GameLoop.TickIntervalMs;
    private readonly IDatabase _redisDb = redis.GetDatabase();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GameLoopWorker started - monitoring for turn timeouts");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Leader Election
                // Try to acquire lock or check if we already hold it
                var lockKey = "leader:gameloop";
                var myId = Environment.MachineName; // Or a Guid if multiple instances on same machine
                
                bool isLeader = false;
                
                if (await _redisDb.StringSetAsync(lockKey, myId, TimeSpan.FromSeconds(15), When.NotExists))
                {
                    isLeader = true;
                }
                else
                {
                    var currentLeader = await _redisDb.StringGetAsync(lockKey);
                    if (currentLeader == myId)
                    {
                        isLeader = true;
                        // Extend lock
                        await _redisDb.KeyExpireAsync(lockKey, TimeSpan.FromSeconds(15));
                    }
                }

                if (isLeader)
                {
                    await CheckRoomsForTimeoutsOptimized(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GameLoopWorker tick");
            }

            await Task.Delay(_tickIntervalMs, stoppingToken);
        }
    }

    private async Task CheckRoomsForTimeoutsOptimized(CancellationToken ct)
    {
        var modules = serviceProvider.GetServices<IGameModule>();

        foreach (var module in modules)
        {
            var engine = serviceProvider.GetKeyedService<IGameEngine>(module.GameName);

            if (engine is not ITurnBasedGameEngine turnEngine)
                continue;

            try
            {
                var roomIds = await roomRegistry.GetRoomsDueForTimeoutAsync(
                    module.GameName,
                    MaxRoomsPerTick);

                if (roomIds.Count == 0) continue;

                await Parallel.ForEachAsync(roomIds, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 10 }, async (roomId, token) =>
                {
                    try
                    {
                        // Lock is still useful to prevent conflict with user actions
                        if (!await roomRegistry.TryAcquireLockAsync(roomId, TimeSpan.FromSeconds(1)))
                            return;

                        try
                        {
                            var result = await turnEngine.CheckTimeoutsAsync(roomId);
                            
                            // Always remove old timeout entry to prevent loop
                            await roomRegistry.UnregisterTurnTimeoutAsync(roomId, module.GameName);

                            if (result != null && result.Success)
                            {
                                logger.LogInformation("Timeout action executed in room {RoomId}: {EventCount} events",
                                    roomId, result.Events.Count);

                                await broadcaster.BroadcastResultAsync(roomId, result);

                                await roomRegistry.UpdateRoomActivityAsync(roomId, module.GameName);

                                // Register next timeout if applicable
                                if (result.NewState is GameStateResponse gameState)
                                {
                                     var turnTimeout = turnEngine.TurnTimeoutSeconds;
                                     if (turnTimeout > 0)
                                     {
                                         var expiry = gameState.Meta.TurnStartedAt.AddSeconds(turnTimeout);
                                         await roomRegistry.RegisterTurnTimeoutAsync(roomId, module.GameName, expiry);
                                     }
                                }

                                if (result.GameEnded != null)
                                {
                                    // ... existing game ended logic ...
                                }
                            }
                        }
                        finally
                        {
                            await roomRegistry.ReleaseLockAsync(roomId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error checking timeout for room {RoomId}", roomId);
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing timeouts for game type {GameType}", module.GameName);
            }
        }
    }
}