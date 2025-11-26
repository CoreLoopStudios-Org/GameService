using GameService.ServiceDefaults.DTOs;

namespace GameService.ApiService.Features.Common;

public interface IGameEventPublisher
{
    Task PublishPlayerUpdatedAsync(PlayerUpdatedMessage message);
}