using GameService.GameCore;
using GameService.ServiceDefaults.Configuration;
using Microsoft.Extensions.Options;

namespace GameService.ApiService.Infrastructure.Workers;

/// <summary>
///     Background worker that checks for turn timeouts across all active games.
///     Runs every few seconds and triggers auto-play/forfeit for AFK players.
/// </summary>
public sealed class GameLoopWorker(
    IServiceProvider serviceProvider,
    IRoomRegistry roomRegistry,
    IGameBroadcaster broadcaster,
    IOptions<GameServiceOptions> options,
    ILogger<GameLoopWorker> logger) : BackgroundService
{
    private readonly int _tickIntervalMs = options.Value.GameLoop.TickIntervalMs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GameLoopWorker started - monitoring for turn timeouts");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllRoomsForTimeouts(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GameLoopWorker tick");
            }

            await Task.Delay(_tickIntervalMs, stoppingToken);
        }
    }

    private async Task CheckAllRoomsForTimeouts(CancellationToken ct)
    {
        var modules = serviceProvider.GetServices<IGameModule>();

        foreach (var module in modules)
        {
            var engine = serviceProvider.GetKeyedService<IGameEngine>(module.GameName);

            if (engine is not ITurnBasedGameEngine turnEngine)
                continue;

            try
            {
                var (roomIds, _) = await roomRegistry.GetRoomIdsPagedAsync(module.GameName, 0, 100);

                foreach (var roomId in roomIds)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        if (!await roomRegistry.TryAcquireLockAsync(roomId, TimeSpan.FromSeconds(1))) continue;

                        try
                        {
                            var result = await turnEngine.CheckTimeoutsAsync(roomId);

                            if (result != null && result.Success)
                            {
                                logger.LogInformation("Timeout action executed in room {RoomId}: {EventCount} events",
                                    roomId, result.Events.Count);

                                await broadcaster.BroadcastResultAsync(roomId, result);
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