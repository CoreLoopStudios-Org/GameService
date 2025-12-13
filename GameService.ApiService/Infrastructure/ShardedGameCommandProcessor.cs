using System.Threading.Channels;
using GameService.GameCore;

namespace GameService.ApiService.Infrastructure;

public class ShardedGameCommandProcessor : IHostedService
{
    private readonly int _shardCount = Environment.ProcessorCount * 2;
    private readonly Channel<GameCommandContext>[] _shards;
    private readonly Task[] _processors;
    private readonly ILogger<ShardedGameCommandProcessor> _logger;

    public ShardedGameCommandProcessor(ILogger<ShardedGameCommandProcessor> logger)
    {
        _logger = logger;
        _shards = new Channel<GameCommandContext>[_shardCount];
        _processors = new Task[_shardCount];

        for (int i = 0; i < _shardCount; i++)
        {
            _shards[i] = Channel.CreateUnbounded<GameCommandContext>();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ShardedGameCommandProcessor with {ShardCount} shards", _shardCount);
        for (int i = 0; i < _shardCount; i++)
        {
            var shardIndex = i;
            _processors[i] = Task.Run(() => ProcessShardAsync(shardIndex, cancellationToken), cancellationToken);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var shard in _shards)
        {
            shard.Writer.TryComplete();
        }
        await Task.WhenAll(_processors);
    }

    public Task<GameActionResult> ProcessCommandAsync(string roomId, Func<Task<GameActionResult>> action)
    {
        var tcs = new TaskCompletionSource<GameActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shardIndex = (Math.Abs(roomId.GetHashCode()) % _shardCount);
        
        var context = new GameCommandContext(roomId, action, tcs);
        if (!_shards[shardIndex].Writer.TryWrite(context))
        {
            return Task.FromResult(GameActionResult.Error("System overloaded"));
        }

        return tcs.Task;
    }

    private async Task ProcessShardAsync(int shardIndex, CancellationToken ct)
    {
        var reader = _shards[shardIndex].Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var context))
                {
                    try
                    {
                        var result = await context.Action();
                        context.Tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing command for room {RoomId}", context.RoomId);
                        context.Tcs.SetException(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in shard {ShardIndex}", shardIndex);
        }
    }

    private record GameCommandContext(string RoomId, Func<Task<GameActionResult>> Action, TaskCompletionSource<GameActionResult> Tcs);
}
