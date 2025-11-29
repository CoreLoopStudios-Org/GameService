using GameService.GameCore;
using GameService.Ludo;
using GameService.ServiceDefaults.DTOs;
using System.Net.Http.Json;

namespace GameService.Web.Services;

public class GameAdminService(HttpClient http)
{
    public async Task<List<GameRoomDto>> GetActiveGamesAsync()
    {
        return await http.GetFromJsonAsync<List<GameRoomDto>>("/admin/games") ?? [];
    }

    public async Task<List<AdminPlayerDto>> GetPlayersAsync()
    {
        return await http.GetFromJsonAsync<List<AdminPlayerDto>>("/admin/players") ?? [];
    }

    public async Task UpdatePlayerCoinsAsync(string userId, long amount)
    {
        var response = await http.PostAsJsonAsync($"/admin/players/{userId}/coins", new UpdateCoinRequest(amount));
        response.EnsureSuccessStatusCode();
    }

    public async Task<System.Text.Json.JsonElement?> GetGameStateAsync(string roomId)
    {
        try 
        {
            return await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"/admin/games/{roomId}");
        }
        catch
        {
            return null;
        }
    }

    public async Task DeletePlayerAsync(string userId)
    {
        var response = await http.DeleteAsync($"/admin/players/{userId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteGameAsync(string roomId)
    {
        var response = await http.DeleteAsync($"/admin/games/{roomId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<SupportedGameDto>> GetSupportedGamesAsync()
    {
        return await http.GetFromJsonAsync<List<SupportedGameDto>>("/games/supported") ?? [];
    }
    
    public async Task CreateGameAsync(int playerCount)
    {
        // This uses the DI injected HttpClient which knows that "apiservice" = localhost:port
        var response = await http.PostAsJsonAsync("/admin/games", new { PlayerCount = playerCount });
        response.EnsureSuccessStatusCode();
    }
}