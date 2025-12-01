using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GameService.Tests;

/// <summary>
/// Tests for WalletTransaction and related economy features
/// </summary>
[TestFixture]
public class EconomyTests
{
    [Test]
    public void WalletTransaction_DefaultValues()
    {
        var tx = new WalletTransaction { UserId = "user1" };
        
        Assert.Multiple(() =>
        {
            Assert.That(tx.TransactionType, Is.EqualTo("Unknown"));
            Assert.That(tx.Description, Is.EqualTo(""));
            Assert.That(tx.ReferenceId, Is.EqualTo(""));
            Assert.That(tx.IdempotencyKey, Is.Null);
            Assert.That(tx.CreatedAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
    }

    [Test]
    public void UpdateCoinRequest_SupportsIdempotencyKey()
    {
        var request = new UpdateCoinRequest(100, "unique-key-123", "room-abc");
        
        Assert.Multiple(() =>
        {
            Assert.That(request.Amount, Is.EqualTo(100));
            Assert.That(request.IdempotencyKey, Is.EqualTo("unique-key-123"));
            Assert.That(request.ReferenceId, Is.EqualTo("room-abc"));
        });
    }

    [Test]
    public void WalletTransactionDto_MapsCorrectly()
    {
        var dto = new WalletTransactionDto(
            Id: 1,
            Amount: 500,
            BalanceAfter: 1500,
            TransactionType: "Credit",
            Description: "Game win",
            ReferenceId: "ROOM123",
            CreatedAt: DateTimeOffset.UtcNow
        );
        
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.EqualTo(1));
            Assert.That(dto.Amount, Is.EqualTo(500));
            Assert.That(dto.BalanceAfter, Is.EqualTo(1500));
            Assert.That(dto.TransactionType, Is.EqualTo("Credit"));
        });
    }

    [Test]
    public void PlayerProfile_SoftDelete_DefaultsFalse()
    {
        var profile = new PlayerProfile { UserId = "user1" };
        
        Assert.Multiple(() =>
        {
            Assert.That(profile.IsDeleted, Is.False);
            Assert.That(profile.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void ArchivedGame_DefaultValues()
    {
        var archived = new ArchivedGame 
        { 
            RoomId = "ABC123",
            GameType = "Ludo"
        };
        
        Assert.Multiple(() =>
        {
            Assert.That(archived.FinalStateJson, Is.EqualTo("{}"));
            Assert.That(archived.EventsJson, Is.EqualTo("[]"));
            Assert.That(archived.PlayerSeatsJson, Is.EqualTo("{}"));
            Assert.That(archived.WinnerUserId, Is.Null);
            Assert.That(archived.TotalPot, Is.EqualTo(0));
        });
    }
}
