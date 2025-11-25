using GameService.ServiceDefaults.Messages;

namespace GameService.Web.Services;

public class PlayerUpdateNotifier
{
    // The event that pages will subscribe to
    public event Action<PlayerUpdatedMessage>? OnPlayerUpdated;

    // Called by the Background Worker
    public void Notify(PlayerUpdatedMessage message)
    {
        OnPlayerUpdated?.Invoke(message);
    }
}