using GameService.GameCore;

namespace GameService.Tests;

/// <summary>
/// Tests for GameRoomMeta and related game core types
/// </summary>
[TestFixture]
public class GameCoreTests
{
    [Test]
    public void GameRoomMeta_CurrentPlayerCount_ReflectsSeats()
    {
        var meta = new GameRoomMeta
        {
            PlayerSeats = new Dictionary<string, int>
            {
                ["user1"] = 0,
                ["user2"] = 1,
                ["user3"] = 2
            }
        };
        
        Assert.That(meta.CurrentPlayerCount, Is.EqualTo(3));
    }

    [Test]
    public void GameRoomMeta_EmptySeats_ReturnsZero()
    {
        var meta = new GameRoomMeta();
        
        Assert.That(meta.CurrentPlayerCount, Is.EqualTo(0));
    }

    [Test]
    public void GameRoomMeta_TurnStartedAt_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var meta = new GameRoomMeta();
        var after = DateTimeOffset.UtcNow;
        
        Assert.That(meta.TurnStartedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(meta.TurnStartedAt, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void GameRoomMeta_DisconnectedPlayers_InitiallyEmpty()
    {
        var meta = new GameRoomMeta();
        
        Assert.That(meta.DisconnectedPlayers, Is.Empty);
    }

    [Test]
    public void GameActionResult_Error_CreatesFailedResult()
    {
        var result = GameActionResult.Error("Something went wrong");
        
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Something went wrong"));
            Assert.That(result.ShouldBroadcast, Is.False);
            Assert.That(result.Events, Is.Empty);
        });
    }

    [Test]
    public void GameActionResult_Ok_CreatesSuccessResult()
    {
        var state = new { SomeProperty = "value" };
        var result = GameActionResult.Ok(state);
        
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.ShouldBroadcast, Is.True);
            Assert.That(result.NewState, Is.EqualTo(state));
        });
    }

    [Test]
    public void GameActionResult_Ok_WithEvents()
    {
        var event1 = new GameEvent("TestEvent", new { Data = 1 });
        var event2 = new GameEvent("AnotherEvent", new { Data = 2 });
        
        var result = GameActionResult.Ok(null, event1, event2);
        
        Assert.That(result.Events, Has.Count.EqualTo(2));
    }

    [Test]
    public void GameEvent_HasTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = new GameEvent("TestEvent", new { });
        var after = DateTimeOffset.UtcNow;
        
        Assert.That(evt.Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(evt.Timestamp, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void GameCommand_GetInt_ReturnsDefaultWhenMissing()
    {
        var json = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        var command = new GameCommand("user1", "action", json);
        
        Assert.That(command.GetInt("missing", 42), Is.EqualTo(42));
    }

    [Test]
    public void GameCommand_GetInt_ReturnsValueWhenPresent()
    {
        var json = System.Text.Json.JsonDocument.Parse("{\"tokenIndex\": 3}").RootElement;
        var command = new GameCommand("user1", "move", json);
        
        Assert.That(command.GetInt("tokenIndex", 0), Is.EqualTo(3));
    }

    [Test]
    public void JoinRoomResult_Ok_CreatesSuccessResult()
    {
        var result = JoinRoomResult.Ok(2);
        
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.SeatIndex, Is.EqualTo(2));
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void JoinRoomResult_Error_CreatesFailedResult()
    {
        var result = JoinRoomResult.Error("Room is full");
        
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.SeatIndex, Is.EqualTo(-1));
            Assert.That(result.ErrorMessage, Is.EqualTo("Room is full"));
        });
    }
}
