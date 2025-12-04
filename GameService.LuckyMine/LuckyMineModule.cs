using System.Text.Json.Serialization;
using GameService.GameCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameService.LuckyMine;

public sealed class LuckyMineModule : IGameModule
{
    public string GameName => "LuckyMine";
    public JsonSerializerContext? JsonContext => LuckyMineJsonContext.Default;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddKeyedSingleton<IGameEngine, LuckyMineEngine>(GameName);
        services.AddKeyedSingleton<IGameRoomService, LuckyMineRoomService>(GameName);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/luckymine").RequireAuthorization("AdminPolicy");

        admin.MapGet("/{roomId}/full-state", async (string roomId, IServiceProvider sp) =>
        {
            var repo = sp.GetRequiredService<IGameRepositoryFactory>().Create<LuckyMineState>("LuckyMine");
            var ctx = await repo.LoadAsync(roomId);

            if (ctx == null) return Results.NotFound();

            var state = ctx.State;
            int safeTiles = state.TotalTiles - state.TotalMines;
            long nextWinnings = 0;
            if (safeTiles > 0 && state.RevealedSafeCount < safeTiles)
            {
                var tempState = state;
                tempState.RevealedSafeCount++;
                double multiplier = 1.0;
                int remaining = safeTiles;
                int total = state.TotalTiles;
                for (int i = 0; i < tempState.RevealedSafeCount; i++)
                {
                    multiplier *= (double)total / remaining;
                    remaining--;
                    total--;
                }
                nextWinnings = (long)(state.EntryCost * multiplier * 0.97);
            }

            var dto = new LuckyMineFullDto
            {
                RevealedMask0 = state.RevealedMask0,
                RevealedMask1 = state.RevealedMask1,
                TotalTiles = state.TotalTiles,
                TotalMines = state.TotalMines,
                RevealedSafeCount = state.RevealedSafeCount,
                EntryCost = state.EntryCost,
                CurrentWinnings = state.CurrentWinnings,
                NextTileWinnings = nextWinnings,
                Status = ((LuckyMineStatus)state.Status).ToString(),
                MineMask0 = state.MineMask0,
                MineMask1 = state.MineMask1
            };

            return Results.Ok(dto);
        });
    }
}