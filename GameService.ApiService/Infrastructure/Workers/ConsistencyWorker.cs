using GameService.ApiService.Features.Games;
using GameService.GameCore;
using GameService.LuckyMine;
using GameService.ServiceDefaults.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace GameService.ApiService.Infrastructure.Workers;

public sealed class ConsistencyWorker(
    IServiceProvider serviceProvider,
    IConnectionMultiplexer redis,
    IGameRepositoryFactory repoFactory,
    ILogger<ConsistencyWorker> logger) : BackgroundService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ConsistencyWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckLuckyMinePendingPayoutsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ConsistencyWorker");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckLuckyMinePendingPayoutsAsync(CancellationToken ct)
    {
        var setKey = "pending_payouts:LuckyMine";
        var roomIds = await _redisDb.SetMembersAsync(setKey);

        if (roomIds.Length == 0) return;

        var repo = repoFactory.Create<LuckyMineState>("LuckyMine");

        foreach (var redisValue in roomIds)
        {
            var roomId = redisValue.ToString();
            try
            {
                var ctx = await repo.LoadAsync(roomId);
                if (ctx == null)
                {
                    // Room gone? Remove from set
                    await _redisDb.SetRemoveAsync(setKey, redisValue);
                    continue;
                }

                var state = ctx.State;
                if (!state.PendingPayout)
                {
                    // Already cleared? Remove from set
                    await _redisDb.SetRemoveAsync(setKey, redisValue);
                    continue;
                }

                // Check if archived
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                
                var isArchived = await db.ArchivedGames.AnyAsync(g => g.RoomId == roomId, ct);
                // Simple check for outbox message payload containing roomId
                var isInOutbox = await db.OutboxMessages.AnyAsync(m => m.EventType == "GameEnded" && m.Payload.Contains(roomId), ct);

                if (isArchived || isInOutbox)
                {
                    // Payout processed or in progress. Clear flag.
                    state.PendingPayout = false;
                    await repo.SaveAsync(roomId, state, ctx.Meta);
                    await _redisDb.SetRemoveAsync(setKey, redisValue);
                    logger.LogInformation("Reconciled payout for room {RoomId} (Already processed)", roomId);
                }
                else
                {
                    // Not processed. Check if stale.
                    if (DateTimeOffset.UtcNow - ctx.Meta.TurnStartedAt > TimeSpan.FromMinutes(1))
                    {
                        logger.LogWarning("Found stale pending payout for room {RoomId}. Triggering archival.", roomId);
                        
                        var archivalService = scope.ServiceProvider.GetRequiredService<IGameArchivalService>();
                        
                        var winnerId = ctx.Meta.PlayerSeats.Keys.FirstOrDefault();
                        
                        await archivalService.EndGameAsync(
                            roomId,
                            "LuckyMine",
                            new { }, 
                            ctx.Meta.PlayerSeats,
                            winnerId,
                            state.CurrentWinnings, 
                            ctx.Meta.TurnStartedAt
                        );
                        
                        state.PendingPayout = false;
                        await repo.SaveAsync(roomId, state, ctx.Meta);
                        await _redisDb.SetRemoveAsync(setKey, redisValue);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reconciling room {RoomId}", roomId);
            }
        }
    }
}
