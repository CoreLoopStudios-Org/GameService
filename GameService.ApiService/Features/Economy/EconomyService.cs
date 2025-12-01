using GameService.ServiceDefaults;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GameService.ApiService.Features.Economy;

public interface IEconomyService
{
    Task<TransactionResult> ProcessTransactionAsync(string userId, long amount);
}

public enum TransactionErrorType 
{ 
    None, 
    InvalidAmount, 
    InsufficientFunds, 
    ConcurrencyConflict, 
    Unknown 
}

public record TransactionResult(
    bool Success, 
    long NewBalance, 
    TransactionErrorType ErrorType = TransactionErrorType.None, 
    string? ErrorMessage = null);

public class EconomyService(
    GameDbContext db, 
    IGameEventPublisher publisher, 
    ILogger<EconomyService> logger) : IEconomyService
{
    public async Task<TransactionResult> ProcessTransactionAsync(string userId, long amount)
    {
        if (amount == 0) 
            return new TransactionResult(false, 0, TransactionErrorType.InvalidAmount, "Amount cannot be zero");

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // 1. Try optimized update first (Common Case)
                // This atomic update prevents race conditions on the balance itself
                var rows = await db.PlayerProfiles
                    .Where(p => p.UserId == userId)
                    .Where(p => amount >= 0 || (p.Coins + amount) >= 0) // Prevent negative balance
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.Coins, p => p.Coins + amount)
                        .SetProperty(p => p.Version, Guid.NewGuid()));
                
                long newBalance = 0;

                if (rows > 0)
                {
                    // Fetch new balance for notification
                    newBalance = await db.PlayerProfiles
                        .Where(p => p.UserId == userId)
                        .Select(p => p.Coins)
                        .FirstAsync();
                }
                else
                {
                    // 2. Fallback: Either insufficient funds OR user doesn't exist
                    var profile = await db.PlayerProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.UserId == userId);

                    if (profile != null)
                    {
                        // User exists, so it must be insufficient funds
                        return new TransactionResult(false, profile.Coins, TransactionErrorType.InsufficientFunds, "Insufficient funds");
                    }

                    // 3. Create Profile (Rare Case - Lazy Initialization)
                    // Double-check user existence in Identity table
                    var user = await db.Users.FindAsync(userId);
                    if (user is null) 
                        return new TransactionResult(false, 0, TransactionErrorType.Unknown, "User account not found.");
                    
                    var initialCoins = 100 + amount;
                    if (initialCoins < 0) 
                        return new TransactionResult(false, 0, TransactionErrorType.InsufficientFunds, "Insufficient funds for initial operation");

                    var newProfile = new PlayerProfile { UserId = userId, Coins = initialCoins, User = user };
                    db.PlayerProfiles.Add(newProfile);
                    await db.SaveChangesAsync();
                    newBalance = initialCoins;
                }

                await transaction.CommitAsync();
                
                // Publish event AFTER commit to ensure consistency.
                // We use Task.Run to fire-and-forget so we don't block the HTTP response 
                // waiting for Redis/SignalR latency.
                _ = Task.Run(() => publisher.PublishPlayerUpdatedAsync(new PlayerUpdatedMessage(
                    userId, 
                    newBalance, 
                    null, 
                    null,
                    PlayerChangeType.Updated,
                    0)));

                return new TransactionResult(true, newBalance, TransactionErrorType.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transaction failed for user {UserId}", userId);
                await transaction.RollbackAsync();
                return new TransactionResult(false, 0, TransactionErrorType.Unknown, "Transaction failed");
            }
        });
    }
}