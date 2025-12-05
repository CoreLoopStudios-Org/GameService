using GameService.ServiceDefaults;
using GameService.ServiceDefaults.Configuration;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameService.ApiService.Features.Economy;

public interface IEconomyService
{
    Task<TransactionResult> ProcessTransactionAsync(string userId, long amount, string? referenceId = null,
        string? idempotencyKey = null);
    
    /// <summary>
    /// Deduct entry fee from player when joining a paid game.
    /// Returns a reservation that can be committed or refunded.
    /// </summary>
    Task<EntryFeeReservation> ReserveEntryFeeAsync(string userId, long entryFee, string roomId);
    
    /// <summary>
    /// Commit a reserved entry fee (player successfully joined room)
    /// </summary>
    Task CommitEntryFeeAsync(EntryFeeReservation reservation);
    
    /// <summary>
    /// Refund a reserved entry fee (player failed to join room)
    /// </summary>
    Task RefundEntryFeeAsync(EntryFeeReservation reservation);
    
    /// <summary>
    /// Award winnings to player(s) when game ends
    /// </summary>
    Task<TransactionResult> AwardWinningsAsync(string userId, long amount, string roomId);
    
    /// <summary>
    /// Process end-of-game payouts for all players
    /// </summary>
    Task<GamePayoutResult> ProcessGamePayoutsAsync(string roomId, string gameType, long totalPot, 
        IReadOnlyDictionary<string, int> playerSeats, string? winnerUserId, 
        IReadOnlyList<string>? winnerRanking = null);
}

public enum TransactionErrorType
{
    None,
    InvalidAmount,
    InsufficientFunds,
    ConcurrencyConflict,
    DuplicateTransaction,
    Unknown
}

public record TransactionResult(
    bool Success,
    long NewBalance,
    TransactionErrorType ErrorType = TransactionErrorType.None,
    string? ErrorMessage = null);

/// <summary>
/// Represents a reserved entry fee that can be committed or refunded
/// </summary>
public record EntryFeeReservation(
    bool Success,
    string UserId,
    long Amount,
    string RoomId,
    string? ReservationId,
    long NewBalance,
    string? ErrorMessage = null);

/// <summary>
/// Result of processing game payouts
/// </summary>
public record GamePayoutResult(
    bool Success,
    IReadOnlyDictionary<string, long> Payouts,
    string? ErrorMessage = null);

public class EconomyService(
    GameDbContext db,
    IGameEventPublisher publisher,
    IOptions<GameServiceOptions> options,
    ILogger<EconomyService> logger) : IEconomyService
{
    private readonly long _initialCoins = options.Value.Economy.InitialCoins;
    private const int MaxRetryAttempts = 3;

    public async Task<TransactionResult> ProcessTransactionAsync(string userId, long amount, string? referenceId = null,
        string? idempotencyKey = null)
    {
        if (amount == 0)
            return new TransactionResult(false, 0, TransactionErrorType.InvalidAmount, "Amount cannot be zero");

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Retry loop for optimistic concurrency conflicts
            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
                try
                {
                    // Check idempotency first
                    if (!string.IsNullOrEmpty(idempotencyKey))
                    {
                        var existing = await db.WalletTransactions
                            .AsNoTracking()
                            .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

                        if (existing != null)
                        {
                            logger.LogWarning("Duplicate transaction attempt with key {Key} for user {UserId}",
                                idempotencyKey, userId);
                            return new TransactionResult(false, existing.BalanceAfter,
                                TransactionErrorType.DuplicateTransaction, "Transaction already processed");
                        }
                    }

                    // Lock the row for update to prevent race conditions
                    // Using raw SQL for SELECT FOR UPDATE (PostgreSQL specific)
                    var profile = await db.PlayerProfiles
                        .FromSqlRaw(
                            "SELECT * FROM \"PlayerProfiles\" WHERE \"UserId\" = {0} AND \"IsDeleted\" = false FOR UPDATE",
                            userId)
                        .FirstOrDefaultAsync();

                    long newBalance;

                    if (profile != null)
                    {
                        // Check sufficient funds for debit
                        if (amount < 0 && profile.Coins + amount < 0)
                        {
                            return new TransactionResult(false, profile.Coins, 
                                TransactionErrorType.InsufficientFunds, "Insufficient funds");
                        }

                        // Update with optimistic concurrency check
                        var currentVersion = profile.Version;
                        profile.Coins += amount;
                        profile.Version = Guid.NewGuid();
                        
                        var rows = await db.SaveChangesAsync();
                        if (rows == 0)
                        {
                            // Concurrency conflict - retry
                            await transaction.RollbackAsync();
                            if (attempt < MaxRetryAttempts - 1)
                            {
                                await Task.Delay(Random.Shared.Next(10, 50));
                                continue;
                            }
                            return new TransactionResult(false, 0, 
                                TransactionErrorType.ConcurrencyConflict, "Concurrent modification detected");
                        }
                        
                        newBalance = profile.Coins;
                    }
                    else
                    {
                        // Create new profile
                        var user = await db.Users.FindAsync(userId);
                        if (user is null)
                            return new TransactionResult(false, 0, TransactionErrorType.Unknown, "User account not found.");

                        var initialCoins = _initialCoins + amount;
                        if (initialCoins < 0)
                            return new TransactionResult(false, 0, TransactionErrorType.InsufficientFunds,
                                "Insufficient funds for initial operation");

                        var newProfile = new PlayerProfile { UserId = userId, Coins = initialCoins, User = user };
                        db.PlayerProfiles.Add(newProfile);
                        await db.SaveChangesAsync();
                        newBalance = initialCoins;
                    }

                    var txType = amount > 0 ? "Credit" : "Debit";
                    var description = amount > 0 ? "Deposit/Win" : "Withdrawal/Entry Fee";

                    // Create wallet transaction record
                    var ledgerEntry = new WalletTransaction
                    {
                        UserId = userId,
                        Amount = amount,
                        BalanceAfter = newBalance,
                        TransactionType = txType,
                        Description = description,
                        ReferenceId = referenceId ?? "System",
                        IdempotencyKey = idempotencyKey,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.WalletTransactions.Add(ledgerEntry);
                    
                    // Add outbox message for reliable event publishing
                    var outboxMessage = new OutboxMessage
                    {
                        EventType = "PlayerUpdated",
                        Payload = System.Text.Json.JsonSerializer.Serialize(new PlayerUpdatedMessage(
                            userId, newBalance, null, null)),
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.OutboxMessages.Add(outboxMessage);
                    
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Try to publish immediately (best effort - outbox processor will retry if this fails)
                    try
                    {
                        await publisher.PublishPlayerUpdatedAsync(new PlayerUpdatedMessage(
                            userId, newBalance, null, null));
                        
                        // Mark as processed if publish succeeded
                        outboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync();
                    }
                    catch (Exception pubEx)
                    {
                        // Outbox processor will retry - this is fine
                        logger.LogDebug(pubEx, "Immediate publish failed for user {UserId}, outbox will retry", userId);
                    }

                    return new TransactionResult(true, newBalance);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync();
                    if (attempt < MaxRetryAttempts - 1)
                    {
                        await Task.Delay(Random.Shared.Next(10, 50));
                        continue;
                    }
                    return new TransactionResult(false, 0, 
                        TransactionErrorType.ConcurrencyConflict, "Concurrent modification detected");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Transaction failed for user {UserId}", userId);
                    await transaction.RollbackAsync();
                    return new TransactionResult(false, 0, TransactionErrorType.Unknown, "Transaction failed");
                }
            }
            
            return new TransactionResult(false, 0, TransactionErrorType.ConcurrencyConflict, "Max retries exceeded");
        });
    }

    public async Task<EntryFeeReservation> ReserveEntryFeeAsync(string userId, long entryFee, string roomId)
    {
        if (entryFee <= 0) 
            return new EntryFeeReservation(true, userId, 0, roomId, null, 0);
        
        var reservationId = $"reserve:{roomId}:{userId}:{Guid.NewGuid():N}";
        var result = await ProcessTransactionAsync(userId, -entryFee, $"ROOM:{roomId}:ENTRY_RESERVE", reservationId);
        
        if (!result.Success)
        {
            return new EntryFeeReservation(false, userId, entryFee, roomId, null, result.NewBalance, 
                result.ErrorMessage ?? "Insufficient funds");
        }
        
        return new EntryFeeReservation(true, userId, entryFee, roomId, reservationId, result.NewBalance);
    }

    public async Task CommitEntryFeeAsync(EntryFeeReservation reservation)
    {
        if (!reservation.Success || reservation.Amount <= 0) return;
        
        // Update the transaction description to mark as committed
        await db.WalletTransactions
            .Where(t => t.IdempotencyKey == reservation.ReservationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Description, "Entry Fee (Confirmed)")
                .SetProperty(t => t.ReferenceId, $"ROOM:{reservation.RoomId}:ENTRY"));
        
        logger.LogInformation("Entry fee committed: User={UserId}, Room={RoomId}, Amount={Amount}", 
            reservation.UserId, reservation.RoomId, reservation.Amount);
    }

    public async Task RefundEntryFeeAsync(EntryFeeReservation reservation)
    {
        if (!reservation.Success || reservation.Amount <= 0) return;
        
        var refundKey = $"refund:{reservation.ReservationId}";
        var result = await ProcessTransactionAsync(
            reservation.UserId, 
            reservation.Amount,  // Credit back the amount
            $"ROOM:{reservation.RoomId}:ENTRY_REFUND", 
            refundKey);
        
        if (result.Success)
        {
            logger.LogInformation("Entry fee refunded: User={UserId}, Room={RoomId}, Amount={Amount}", 
                reservation.UserId, reservation.RoomId, reservation.Amount);
        }
        else
        {
            logger.LogError("Failed to refund entry fee: User={UserId}, Room={RoomId}, Amount={Amount}, Error={Error}", 
                reservation.UserId, reservation.RoomId, reservation.Amount, result.ErrorMessage);
        }
    }

    public async Task<TransactionResult> AwardWinningsAsync(string userId, long amount, string roomId)
    {
        if (amount <= 0) return new TransactionResult(true, 0);
        
        var idempotencyKey = $"win:{roomId}:{userId}";
        return await ProcessTransactionAsync(userId, amount, $"ROOM:{roomId}:WIN", idempotencyKey);
    }

    public async Task<GamePayoutResult> ProcessGamePayoutsAsync(
        string roomId, 
        string gameType, 
        long totalPot,
        IReadOnlyDictionary<string, int> playerSeats, 
        string? winnerUserId,
        IReadOnlyList<string>? winnerRanking = null)
    {
        if (totalPot <= 0)
            return new GamePayoutResult(true, new Dictionary<string, long>());

        var payouts = new Dictionary<string, long>();
        var playerCount = playerSeats.Count;
        
        // Calculate house rake (3%)
        var rake = (long)(totalPot * 0.03);
        var prizePool = totalPot - rake;

        try
        {
            if (winnerRanking != null && winnerRanking.Count > 0)
            {
                // Multi-winner payout distribution
                payouts = CalculateRankedPayouts(winnerRanking, prizePool);
            }
            else if (!string.IsNullOrEmpty(winnerUserId))
            {
                // Single winner takes all (minus rake)
                payouts[winnerUserId] = prizePool;
            }
            else
            {
                // No winner - refund all players (minus rake per player)
                var refundPerPlayer = prizePool / playerCount;
                foreach (var userId in playerSeats.Keys)
                {
                    payouts[userId] = refundPerPlayer;
                }
            }

            // Process all payouts
            foreach (var (userId, amount) in payouts)
            {
                var result = await AwardWinningsAsync(userId, amount, roomId);
                if (!result.Success)
                {
                    logger.LogError("Failed to pay {Amount} to {UserId} for room {RoomId}: {Error}",
                        amount, userId, roomId, result.ErrorMessage);
                }
            }

            logger.LogInformation(
                "Game payouts processed: Room={RoomId}, Type={GameType}, TotalPot={Pot}, Rake={Rake}, Payouts={Count}",
                roomId, gameType, totalPot, rake, payouts.Count);

            return new GamePayoutResult(true, payouts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process game payouts for room {RoomId}", roomId);
            return new GamePayoutResult(false, payouts, ex.Message);
        }
    }

    private static Dictionary<string, long> CalculateRankedPayouts(IReadOnlyList<string> ranking, long prizePool)
    {
        var payouts = new Dictionary<string, long>();
        
        // Standard payout structure based on player count
        var payoutPercentages = ranking.Count switch
        {
            1 => new[] { 1.0 },
            2 => new[] { 0.7, 0.3 },
            3 => new[] { 0.5, 0.3, 0.2 },
            4 => new[] { 0.4, 0.3, 0.2, 0.1 },
            _ => CalculatePayoutPercentages(ranking.Count)
        };

        for (int i = 0; i < ranking.Count && i < payoutPercentages.Length; i++)
        {
            var payout = (long)(prizePool * payoutPercentages[i]);
            if (payout > 0)
            {
                payouts[ranking[i]] = payout;
            }
        }

        return payouts;
    }

    private static double[] CalculatePayoutPercentages(int playerCount)
    {
        // Top 50% of players get paid
        var paidPositions = Math.Max(1, playerCount / 2);
        var percentages = new double[paidPositions];
        
        double total = 0;
        for (int i = 0; i < paidPositions; i++)
        {
            percentages[i] = 1.0 / (i + 1); // Harmonic series
            total += percentages[i];
        }
        
        // Normalize to sum to 1.0
        for (int i = 0; i < paidPositions; i++)
        {
            percentages[i] /= total;
        }
        
        return percentages;
    }
}