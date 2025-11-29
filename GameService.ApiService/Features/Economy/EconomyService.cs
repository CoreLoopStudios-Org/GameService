using GameService.ApiService.Features.Common;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GameService.ApiService.Features.Economy;

public interface IEconomyService
{
    Task<TransactionResult> ProcessTransactionAsync(string userId, long amount);
}

public enum TransactionErrorType { None, InvalidAmount, InsufficientFunds, ConcurrencyConflict, Unknown }

public record TransactionResult(bool Success, long NewBalance, TransactionErrorType ErrorType = TransactionErrorType.None, string? ErrorMessage = null);

public class EconomyService(GameDbContext db, IGameEventPublisher publisher) : IEconomyService
{
    public async Task<TransactionResult> ProcessTransactionAsync(string userId, long amount)
    {
        if (amount == 0) return new TransactionResult(false, 0, TransactionErrorType.InvalidAmount, "Amount cannot be zero");

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Use ExecuteUpdateAsync for atomic update
            // Note: This doesn't check for insufficient funds in the DB query itself easily without raw SQL or stored proc in some EF versions,
            // but we can do a conditional update.
            
            // However, for simplicity and correctness with checks:
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Lock the row? Or just use optimistic concurrency?
                // The user asked for atomic updates to fix concurrency issues.
                // ExecuteUpdate is atomic.
                
                if (amount < 0)
                {
                    // Check balance first
                    var currentCoins = await db.PlayerProfiles
                        .Where(p => p.UserId == userId)
                        .Select(p => p.Coins)
                        .FirstOrDefaultAsync();
                        
                    if (currentCoins + amount < 0)
                    {
                         return new TransactionResult(false, currentCoins, TransactionErrorType.InsufficientFunds, "Insufficient funds");
                    }
                }

                var rows = await db.PlayerProfiles
                    .Where(p => p.UserId == userId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.Coins, p => p.Coins + amount)
                        .SetProperty(p => p.Version, Guid.NewGuid()));
                
                if (rows == 0)
                {
                    // Profile might not exist, create it?
                    // Separation of concerns: Create profile on registration or first login.
                    // But legacy code created it here.
                    var user = await db.Users.FindAsync(userId);
                    if (user is null) return new TransactionResult(false, 0, TransactionErrorType.Unknown, "User account not found.");
                    
                    var newProfile = new PlayerProfile { UserId = userId, Coins = 100 + amount, User = user };
                    db.PlayerProfiles.Add(newProfile);
                    await db.SaveChangesAsync();
                    
                    await transaction.CommitAsync();
                    return new TransactionResult(true, newProfile.Coins, TransactionErrorType.None);
                }

                await transaction.CommitAsync();
                
                // Fetch new balance for return
                var newBalance = await db.PlayerProfiles.Where(p => p.UserId == userId).Select(p => p.Coins).FirstAsync();

                var message = new PlayerUpdatedMessage(
                    userId, 
                    newBalance, 
                    null, 
                    null);

                await publisher.PublishPlayerUpdatedAsync(message);

                return new TransactionResult(true, newBalance, TransactionErrorType.None);
            }
            catch (Exception ex)
            {
                return new TransactionResult(false, 0, TransactionErrorType.Unknown, ex.Message);
            }
        });
    }
}