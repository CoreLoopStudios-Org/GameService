using GameService.ServiceDefaults.DTOs;

namespace GameService.ServiceDefaults;

public interface IGameEventPublisher
{
    Task PublishPlayerUpdatedAsync(PlayerUpdatedMessage message);
}