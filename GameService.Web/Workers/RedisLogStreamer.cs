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
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sub = redis.GetSubscriber();
                var channel = await sub.SubscribeAsync(RedisChannel.Literal(GameConstants.PlayerUpdatesChannel));

                logger.LogInformation("✅ [RedisLogStreamer] Connected and listening on channel: {Channel}", GameConstants.PlayerUpdatesChannel);

                await foreach (var message in channel.WithCancellation(stoppingToken))
                {
                    if (message.Message.IsNullOrEmpty) continue;

                    try
                    {
                        var payload = (string)message.Message!;
                        logger.LogInformation("⚡ [RedisLogStreamer] Received: {Payload}", payload);

                        var update = JsonSerializer.Deserialize<PlayerUpdatedMessage>(payload, _jsonOptions);

                        if (update != null)
                        {
                            notifier.Notify(update);
                        }
                    }
                    catch (JsonException jex)
                    {
                        logger.LogError(jex, "❌ [RedisLogStreamer] JSON Deserialization failed for message: {Message}", message.Message);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "❌ [RedisLogStreamer] Error processing message");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "⚠️ [RedisLogStreamer] Connection failed. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}