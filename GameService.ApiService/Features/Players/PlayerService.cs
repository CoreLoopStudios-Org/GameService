using GameService.ServiceDefaults;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GameService.ApiService.Features.Players;

public interface IPlayerService
{
    Task<PlayerProfileResponse?> GetProfileAsync(string userId);
}

public class PlayerService(GameDbContext db, IGameEventPublisher publisher) : IPlayerService
{
    public async Task<PlayerProfileResponse?> GetProfileAsync(string userId)
    {
        var profile = await db.PlayerProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is not null)
        {
            var message = new PlayerUpdatedMessage(
                profile.UserId,
                profile.Coins,
                profile.User?.UserName ?? "Unknown",
                profile.User?.Email ?? "Unknown",
                PlayerChangeType.Updated,
                profile.Id
            );

            await publisher.PublishPlayerUpdatedAsync(message);

            return new PlayerProfileResponse(profile.UserId, profile.Coins);
        }

        return null;
    }
}