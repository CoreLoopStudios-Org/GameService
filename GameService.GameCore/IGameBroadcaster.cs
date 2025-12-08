namespace GameService.GameCore;

public interface IGameBroadcaster
{
    Task BroadcastStateAsync(string roomId, object state);
    Task BroadcastEventAsync(string roomId, GameEvent gameEvent);
    Task BroadcastResultAsync(string roomId, GameActionResult result);
}