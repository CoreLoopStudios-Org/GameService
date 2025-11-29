using GameService.GameCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameService.Ludo;

public class LudoModule : IGameModule
{
    public string GameName => "Ludo";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ILudoRepository, RedisLudoRepository>();
        services.AddSingleton<LudoRoomService>();
        // Register as IGameRoomService so generic admin can find it
        services.AddSingleton<IGameRoomService>(sp => sp.GetRequiredService<LudoRoomService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/games/ludo").RequireAuthorization();
        group.MapHub<LudoHub>("/hubs/ludo");
        
        // Ludo specific endpoints if any
    }
}