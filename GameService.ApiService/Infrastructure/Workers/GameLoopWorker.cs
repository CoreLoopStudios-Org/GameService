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
                var roomIds = await roomRegistry.GetRoomsNeedingTimeoutCheckAsync(
                    module.GameName,
                    MaxRoomsPerTick);

                if (roomIds.Count == 0) continue;

                foreach (var roomId in roomIds)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        if (!await roomRegistry.TryAcquireLockAsync(roomId, TimeSpan.FromSeconds(1)))
                            continue;

                        try
                        {
                            var result = await turnEngine.CheckTimeoutsAsync(roomId);

                            if (result != null && result.Success)
                            {
                                logger.LogInformation("Timeout action executed in room {RoomId}: {EventCount} events",
                                    roomId, result.Events.Count);

                                await broadcaster.BroadcastResultAsync(roomId, result);

                                await roomRegistry.UpdateRoomActivityAsync(roomId, module.GameName);

                                if (result.GameEnded != null)
                                {
                                    using var scope = serviceProvider.CreateScope();
                                    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                                    var info = result.GameEnded;

                                    var outboxPayload = JsonSerializer.Serialize(new GameEndedPayload(
                                        info.RoomId,
                                        info.GameType,
                                        info.PlayerSeats,
                                        info.WinnerUserId,
                                        info.TotalPot,
                                        info.StartedAt,
                                        info.WinnerRanking,
                                        result.NewState));

                                    db.OutboxMessages.Add(new OutboxMessage
                                    {
                                        EventType = "GameEnded",
                                        Payload = outboxPayload,
                                        CreatedAt = DateTimeOffset.UtcNow
                                    });

                                    await db.SaveChangesAsync(ct);
                                    logger.LogInformation("Game {RoomId} ended via timeout. Scheduled payout.", roomId);
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
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing timeouts for game type {GameType}", module.GameName);
            }
        }
    }
}