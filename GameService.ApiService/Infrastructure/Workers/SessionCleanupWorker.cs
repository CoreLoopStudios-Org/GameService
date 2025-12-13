using GameService.GameCore;
using GameService.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using GameService.ApiService.Hubs;

namespace GameService.ApiService.Infrastructure.Workers;

public sealed class SessionCleanupWorker(
    IServiceProvider serviceProvider,
    IRoomRegistry roomRegistry,
    ILogger<SessionCleanupWorker> logger) : BackgroundService
{
    private readonly int _tickIntervalMs = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SessionCleanupWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in SessionCleanupWorker");
            }

            await Task.Delay(_tickIntervalMs, stoppingToken);
        }
    }

    private async Task ProcessExpiredSessionsAsync(CancellationToken ct)
    {
        var expiredUsers = await roomRegistry.GetExpiredDisconnectedPlayersAsync(50);
        if (expiredUsers.Count == 0) return;

        foreach (var userId in expiredUsers)
        {
            if (ct.IsCancellationRequested) return;

            var roomId = await roomRegistry.TryGetAndRemoveDisconnectedPlayerAsync(userId);
            if (roomId == null) continue;

            try
            {
                using var scope = serviceProvider.CreateScope();
                var gameType = await roomRegistry.GetGameTypeAsync(roomId);
                if (gameType != null)
                {
                    var roomService = scope.ServiceProvider.GetKeyedService<IGameRoomService>(gameType);
                    if (roomService != null)
                        await roomService.LeaveRoomAsync(roomId, userId);
                }

                await roomRegistry.RemoveUserRoomAsync(userId);

                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub, IGameClient>>();
                // We don't have UserName here easily, but PlayerLeftPayload might handle null or we can fetch it.
                // For now, send "Unknown" or fetch user.
                // Fetching user is expensive. Let's send "Unknown" or just userId.
                await hubContext.Clients.Group(roomId).PlayerLeft(new PlayerLeftPayload(userId, "Unknown"));

                logger.LogInformation("Session cleanup: Removed player {UserId} from room {RoomId}", userId, roomId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cleaning up session for user {UserId}", userId);
            }
        }
    }
}
