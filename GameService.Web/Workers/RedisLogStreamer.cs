using System.Text.Json;
using GameService.ServiceDefaults;
using GameService.ServiceDefaults.DTOs;
using GameService.Web.Services;
using StackExchange.Redis;

namespace GameService.Web.Workers;

public class RedisLogStreamer(
    IConnectionMultiplexer redis, 
    PlayerUpdateNotifier notifier, 
    ILogger<RedisLogStreamer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNameCaseInsensitive = true 
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        var channel = await sub.SubscribeAsync(GameConstants.PlayerUpdatesChannel);

        logger.LogInformation("🔴 Connected to Redis. Listening for player updates...");

        try
        {
            await foreach (var message in channel.WithCancellation(stoppingToken))
            {
                if (message.Message.IsNullOrEmpty) continue;

                try
                {
                    var update = JsonSerializer.Deserialize<PlayerUpdatedMessage>(
                        (string)message.Message!, 
                        _jsonOptions);

                    if (update is not null)
                    {
                        notifier.Notify(update);
                    }
                }
                catch (JsonException)
                {
                    logger.LogWarning("Received invalid JSON in player_updates");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (redis.IsConnected)
                await sub.UnsubscribeAsync(GameConstants.PlayerUpdatesChannel);
        }
    }
}