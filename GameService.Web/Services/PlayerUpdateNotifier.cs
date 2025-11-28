using GameService.ServiceDefaults.DTOs;

namespace GameService.Web.Services;

public class PlayerUpdateNotifier
{
    public event Action<PlayerUpdatedMessage>? OnPlayerUpdated;

    public void Notify(PlayerUpdatedMessage message)
    {
        OnPlayerUpdated?.Invoke(message);
    }
}