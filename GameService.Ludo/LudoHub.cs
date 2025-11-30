using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameService.Ludo;

[Authorize]
public class LudoHub(LudoRoomService roomService) : Hub
{
    private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public async Task<string> CreateGame()
    {
        var roomId = await roomService.CreateRoomAsync(UserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.Caller.SendAsync("RoomCreated", roomId);
        return roomId;
    }

    public async Task<bool> JoinGame(string roomId)
    {
        if (await roomService.JoinRoomAsync(roomId, UserId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            var ctx = await roomService.LoadGameAsync(roomId);
            if(ctx != null)
                await Clients.Caller.SendAsync("GameState", LudoStateSerializer.Serialize(ctx.State));
                
            await Clients.Group(roomId).SendAsync("PlayerJoined", UserId);
            return true;
        }
        return false;
    }

    public async Task RollDice(string roomId)
    {
        var result = await roomService.PerformRollAsync(roomId, UserId, bypassChecks: false);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("Error", result.Message);
        }
    }

    public async Task MoveToken(string roomId, int tokenIndex)
    {
        var result = await roomService.PerformMoveAsync(roomId, UserId, tokenIndex, bypassChecks: false);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("Error", result.Message);
        }
    }
}