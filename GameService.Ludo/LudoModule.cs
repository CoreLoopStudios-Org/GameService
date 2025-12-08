using System.Text.Json;
using System.Text.Json.Serialization;
using GameService.GameCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameService.Ludo;

public sealed class LudoModule : IGameModule
{
    public string GameName => "Ludo";
    public JsonSerializerContext? JsonContext => LudoJsonContext.Default;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddOptions<LudoOptions>()
            .Configure<IConfiguration>((opts, config) =>
                config.GetSection(LudoOptions.SectionName).Bind(opts));

        services.AddKeyedSingleton<IGameEngine, LudoGameEngine>(GameName);
        services.AddKeyedSingleton<IGameRoomService, LudoRoomService>(GameName);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/ludo").RequireAuthorization("AdminPolicy");

        admin.MapPost("/{roomId}/roll", async (string roomId, int? value, IServiceProvider sp, IGameBroadcaster bc) =>
        {
            object? payload = value.HasValue ? new { ForcedValue = value.Value } : null;
            var jsonPayload = payload != null 
                ? JsonSerializer.SerializeToElement(payload) 
                : default;

            var command = new GameCommand(GameCoreConstants.AdminUserId, "roll", jsonPayload);
            var res = await sp.GetRequiredKeyedService<IGameEngine>("Ludo").ExecuteAsync(roomId, command);

            if (res.Success) await bc.BroadcastResultAsync(roomId, res);
            return res.Success ? Results.Ok(res) : Results.BadRequest(res);
        });

        admin.MapPost("/{roomId}/move/{tokenIndex:int}",
            async (string roomId, int tokenIndex, IServiceProvider sp, IGameBroadcaster bc) =>
            {
                var json = $"{{\"tokenIndex\": {tokenIndex}}}";
                var payload = JsonDocument.Parse(json).RootElement;

                var command = new GameCommand(GameCoreConstants.AdminUserId, "move", payload);
                var res = await sp.GetRequiredKeyedService<IGameEngine>("Ludo").ExecuteAsync(roomId, command);

                if (res.Success) await bc.BroadcastResultAsync(roomId, res);
                return res.Success ? Results.Ok(res) : Results.BadRequest(res);
            });
    }
}