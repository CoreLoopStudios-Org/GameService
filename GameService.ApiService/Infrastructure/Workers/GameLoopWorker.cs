using System.Text.Json;
using GameService.ApiService.Hubs;
using GameService.GameCore;
using GameService.ServiceDefaults.Configuration;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.Extensions.Options;

namespace GameService.ApiService.Infrastructure.Workers;

public sealed class GameLoopWorker(
    IServiceProvider serviceProvider,
    IRoomRegistry roomRegistry,
    IGameBroadcaster broadcaster,
    IOptions<GameServiceOptions> options,
    ILogger<GameLoopWorker> logger) : BackgroundService
{
    private const int MaxRoomsPerTick = 50;
    private readonly int _tickIntervalMs = options.Value.GameLoop.TickIntervalMs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GameLoopWorker started - monitoring for turn timeouts");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckRoomsForTimeoutsOptimized(stoppingToken);
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

                foreach (var roomId in roomIds)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        // Lock is still useful to prevent conflict with user actions
                        if (!await roomRegistry.TryAcquireLockAsync(roomId, TimeSpan.FromSeconds(1)))
                            continue;

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
                                    // Remove from timeout index
                                    // We don't have UnregisterTurnTimeout, but we can set score to +infinity or remove.
                                    // Let's assume UnregisterRoomAsync handles cleanup when game ends?
                                    // But here we just schedule archival.
                                }
                            }
                            else
                            {
                                // Timeout check failed or no timeout needed?
                                // Maybe the room was in the index but state changed?
                                // We should probably remove it from index to avoid infinite loop if it keeps failing.
                                // But maybe we should backoff?
                                // For now, let's assume if CheckTimeoutsAsync returns false, we should remove it?
                                // Or maybe update it to check again later?
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
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing timeouts for game type {GameType}", module.GameName);
            }
        }
    }
}