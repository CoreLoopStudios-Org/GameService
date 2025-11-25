using System.Text.Json;
using GameService.ServiceDefaults.Messages;
using GameService.Web.Services;
using StackExchange.Redis;

namespace GameService.Web.Workers;

public class RedisLogStreamer(
    IConnectionMultiplexer redis, 
    PlayerUpdateNotifier notifier, 
    ILogger<RedisLogStreamer> logger) : BackgroundService
{
    private readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNameCaseInsensitive = true // Safety: Handles Pascal/Camel case differences
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        
        // 1. Create the subscription channel
        // This returns a wrapper that allows async iteration
        var channel = await sub.SubscribeAsync("player_updates");

        logger.LogInformation("🔴 Connected to Redis. Listening for player updates...");

        try
        {
            // 2. Consume messages using await foreach (The correct way)
            // This loop will run forever until the app stops
            await foreach (var message in channel.WithCancellation(stoppingToken))
            {
                if (message.Message.IsNullOrEmpty) continue;

                try
                {
                    var json = message.Message.ToString();
                    var update = JsonSerializer.Deserialize<PlayerUpdatedMessage>(json, _jsonOptions);

                    if (update is not null)
                    {
                        notifier.Notify(update);
                        logger.LogDebug("⚡ Processed update for {UserId}", update.UserId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to parse Redis message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            // Cleanup subscription on stop
            await sub.UnsubscribeAsync("player_updates");
        }
    }
}