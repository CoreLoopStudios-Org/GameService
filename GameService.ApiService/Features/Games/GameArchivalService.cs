using System.Text.Json;
using GameService.GameCore;
using GameService.ServiceDefaults.Data;

namespace GameService.ApiService.Features.Games;

public interface IGameArchivalService
{
    Task ArchiveGameAsync(string roomId, string gameType, object finalState, IReadOnlyDictionary<string, int> playerSeats, 
        string? winnerUserId, long totalPot, DateTimeOffset startedAt);
}

public class GameArchivalService(GameDbContext db, ILogger<GameArchivalService> logger) : IGameArchivalService
{
    public async Task ArchiveGameAsync(
        string roomId, 
        string gameType, 
        object finalState, 
        IReadOnlyDictionary<string, int> playerSeats, 
        string? winnerUserId, 
        long totalPot,
        DateTimeOffset startedAt)
    {
        try
        {
            var archivedGame = new ArchivedGame
            {
                RoomId = roomId,
                GameType = gameType,
                FinalStateJson = JsonSerializer.Serialize(finalState),
                PlayerSeatsJson = JsonSerializer.Serialize(playerSeats),
                WinnerUserId = winnerUserId,
                TotalPot = totalPot,
                StartedAt = startedAt,
                EndedAt = DateTimeOffset.UtcNow
            };

            db.ArchivedGames.Add(archivedGame);
            await db.SaveChangesAsync();

            logger.LogInformation("Archived game {RoomId} (Type: {GameType}, Winner: {Winner}, Pot: {Pot})", 
                roomId, gameType, winnerUserId ?? "None", totalPot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to archive game {RoomId}", roomId);
        }
    }
}
