using System.Security.Claims;
using GameService.ApiService.Features.Common;
using GameService.GameCore;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameService.ApiService.Features.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").RequireAuthorization("AdminPolicy");

        group.MapGet("/games", GetGames);
        // group.MapPost("/games", CreateGame); // Generic creation is complex, maybe per game type?
        // group.MapDelete("/games/{roomId}", DeleteGame); // Generic delete

        group.MapGet("/games/{roomId}", GetGameState);
        group.MapDelete("/games/{roomId}", DeleteGame);
        group.MapGet("/players", GetPlayers);
        group.MapPost("/players/{userId}/coins", UpdatePlayerCoins);
        group.MapDelete("/players/{userId}", DeletePlayer);
    }

    private static async Task<IResult> GetPlayers(
        GameDbContext db,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var players = await db.PlayerProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminPlayerDto(
                p.Id,
                p.UserId,
                p.User.UserName ?? "Unknown",
                p.User.Email ?? "No Email",
                p.Coins
            ))
            .ToListAsync();

        return Results.Ok(players);
    }

    private static async Task<IResult> UpdatePlayerCoins(
        string userId, 
        [FromBody] UpdateCoinRequest req,
        GameDbContext db,
        IGameEventPublisher publisher)
    {
        // Atomic update using ExecuteUpdateAsync
        var rows = await db.PlayerProfiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Coins, p => p.Coins + req.Amount)
                .SetProperty(p => p.Version, Guid.NewGuid()));

        if (rows == 0) return Results.NotFound();

        // We need to fetch the new balance to return it, or just return success.
        // For now, let's fetch it to maintain contract, or change contract.
        // Fetching is okay here as it's admin action, not high freq game loop.
        var profile = await db.PlayerProfiles.Include(p => p.User).AsNoTracking().FirstAsync(p => p.UserId == userId);

        var message = new PlayerUpdatedMessage(
            profile.UserId, 
            profile.Coins, 
            profile.User?.UserName ?? "Unknown", 
            profile.User?.Email ?? "Unknown");

        await publisher.PublishPlayerUpdatedAsync(message);

        return Results.Ok(new { NewBalance = profile.Coins });
    }

    private static async Task<IResult> DeletePlayer(
        string userId, 
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Results.NotFound();

        // Soft delete is better, but for now we stick to delete but ensure it's safe?
        // User asked for fix. Deleting user cascades to profile.
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded) return Results.BadRequest(result.Errors);

        return Results.Ok();
    }

    private static async Task<IResult> GetGameState(string roomId, IEnumerable<IGameRoomService> services)
    {
        foreach (var service in services)
        {
            var state = await service.GetGameStateAsync(roomId);
            if (state != null) return Results.Ok(state);
        }
        return Results.NotFound();
    }

    private static async Task<IResult> DeleteGame(string roomId, IEnumerable<IGameRoomService> services)
    {
        // We don't know which service owns the room, so we try all?
        // Or we could ask them if they have it.
        // For now, try all.
        foreach (var service in services)
        {
            // We can check if it exists first or just try delete.
            // Assuming Delete is idempotent or handles not found gracefully.
            await service.DeleteRoomAsync(roomId);
        }
        return Results.Ok();
    }
    private static async Task<IResult> GetGames(IEnumerable<IGameRoomService> services)
    {
        var allGames = new List<GameRoomDto>();
        foreach (var service in services)
        {
            allGames.AddRange(await service.GetActiveGamesAsync());
        }
        return Results.Ok(allGames);
    }
}