using System.Text.Json;
using GameService.ServiceDefaults;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GameService.ApiService.Infrastructure.Workers;

/// <summary>
/// Background worker that processes outbox messages for reliable event publishing.
/// Ensures events are eventually delivered even if the initial publish failed.
/// Uses transactional outbox pattern for guaranteed delivery.
/// </summary>
public sealed class OutboxProcessorWorker(
    IServiceProvider serviceProvider,
    IGameEventPublisher publisher,
    ILogger<OutboxProcessorWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan ProcessingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    private DateTime _lastCleanup = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxProcessorWorker started");

        // Wait for app to stabilize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);

                // Periodic cleanup of old processed messages
                if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
                {
                    await CleanupOldMessagesAsync(stoppingToken);
                    _lastCleanup = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OutboxProcessorWorker");
            }

            await Task.Delay(ProcessingInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        // Get pending messages that haven't exceeded max attempts
        var pendingMessages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0) return;

        logger.LogDebug("Processing {Count} outbox messages", pendingMessages.Count);

        foreach (var message in pendingMessages)
        {
            try
            {
                await PublishMessageAsync(message);
                
                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.LastError = null;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                
                logger.LogWarning(ex, 
                    "Failed to publish outbox message {Id} (attempt {Attempt}/{Max})", 
                    message.Id, message.Attempts, MaxAttempts);
            }
        }

        await db.SaveChangesAsync(ct);

        var processed = pendingMessages.Count(m => m.ProcessedAt != null);
        if (processed > 0)
        {
            logger.LogInformation("Processed {Processed}/{Total} outbox messages", processed, pendingMessages.Count);
        }
    }

    private async Task PublishMessageAsync(OutboxMessage message)
    {
        switch (message.EventType)
        {
            case "PlayerUpdated":
                var playerUpdate = JsonSerializer.Deserialize<PlayerUpdatedMessage>(message.Payload);
                if (playerUpdate != null)
                {
                    await publisher.PublishPlayerUpdatedAsync(playerUpdate);
                }
                break;
            
            // Add more event types as needed
            default:
                logger.LogWarning("Unknown outbox event type: {EventType}", message.EventType);
                break;
        }
    }

    private async Task CleanupOldMessagesAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var cutoff = DateTimeOffset.UtcNow - RetentionPeriod;

        var deleted = await db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation("Cleaned up {Count} old outbox messages", deleted);
        }

        // Also clean up failed messages that exceeded max attempts
        var failedDeleted = await db.OutboxMessages
            .Where(m => m.Attempts >= MaxAttempts && m.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (failedDeleted > 0)
        {
            logger.LogWarning("Cleaned up {Count} failed outbox messages (exceeded max attempts)", failedDeleted);
        }
    }
}
