using GameService.GameCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        services.AddSingleton<IGameRoomService>(sp => sp.GetRequiredService<LudoRoomService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<LudoHub>("/hubs/ludo").RequireAuthorization();

        // Admin Endpoints for Ludo specific controls
        var admin = endpoints.MapGroup("/admin/ludo").RequireAuthorization("AdminPolicy");

        admin.MapPost("/{roomId}/roll", async (string roomId, [FromServices] LudoRoomService service) => 
        {
            // BypassChecks = true means act as the CurrentPlayer (God Mode)
            var result = await service.PerformRollAsync(roomId, "ADMIN", bypassChecks: true);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        admin.MapPost("/{roomId}/move/{tokenIndex}", async (string roomId, int tokenIndex, [FromServices] LudoRoomService service) => 
        {
            var result = await service.PerformMoveAsync(roomId, "ADMIN", tokenIndex, bypassChecks: true);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });
    }
}