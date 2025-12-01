using GameService.LuckyMine;
using GameService.GameCore;

namespace GameService.Tests;

/// <summary>
/// Unit tests for the LuckyMine game state and logic
/// </summary>
[TestFixture]
public class LuckyMineStateTests
{
    [Test]
    public void IsMine_CorrectlyIdentifiesMines_LowerBits()
    {
        var state = new LuckyMineState
        {
            MineMask0 = 0b1010, // Mines at positions 1 and 3
            TotalTiles = 100
        };
        
        Assert.Multiple(() =>
        {
            Assert.That(state.IsMine(0), Is.False);
            Assert.That(state.IsMine(1), Is.True);
            Assert.That(state.IsMine(2), Is.False);
            Assert.That(state.IsMine(3), Is.True);
        });
    }

    [Test]
    public void IsMine_CorrectlyIdentifiesMines_UpperBits()
    {
        var state = new LuckyMineState
        {
            MineMask1 = 0b0101, // Mines at positions 64 and 66
            TotalTiles = 100
        };
        
        Assert.Multiple(() =>
        {
            Assert.That(state.IsMine(64), Is.True);
            Assert.That(state.IsMine(65), Is.False);
            Assert.That(state.IsMine(66), Is.True);
            Assert.That(state.IsMine(67), Is.False);
        });
    }

    [Test]
    public void IsRevealed_InitiallyAllHidden()
    {
        var state = new LuckyMineState { TotalTiles = 100 };
        
        for (int i = 0; i < 100; i++)
        {
            Assert.That(state.IsRevealed(i), Is.False, $"Tile {i} should be hidden");
        }
    }

    [Test]
    public void SetRevealed_CorrectlyReveals_LowerBits()
    {
        var state = new LuckyMineState { TotalTiles = 100 };
        
        state.SetRevealed(5);
        state.SetRevealed(10);
        
        Assert.Multiple(() =>
        {
            Assert.That(state.IsRevealed(5), Is.True);
            Assert.That(state.IsRevealed(10), Is.True);
            Assert.That(state.IsRevealed(6), Is.False);
        });
    }

    [Test]
    public void SetRevealed_CorrectlyReveals_UpperBits()
    {
        var state = new LuckyMineState { TotalTiles = 100 };
        
        state.SetRevealed(70);
        state.SetRevealed(80);
        
        Assert.Multiple(() =>
        {
            Assert.That(state.IsRevealed(70), Is.True);
            Assert.That(state.IsRevealed(80), Is.True);
            Assert.That(state.IsRevealed(75), Is.False);
        });
    }

    [Test]
    public void IsDead_InitiallyAllAlive()
    {
        var state = new LuckyMineState();
        
        for (int i = 0; i < 10; i++)
        {
            Assert.That(state.IsDead(i), Is.False, $"Player {i} should be alive");
        }
    }

    [Test]
    public void SetDead_CorrectlyMarksPlayerDead()
    {
        var state = new LuckyMineState();
        
        state.SetDead(2);
        state.SetDead(5);
        
        Assert.Multiple(() =>
        {
            Assert.That(state.IsDead(0), Is.False);
            Assert.That(state.IsDead(1), Is.False);
            Assert.That(state.IsDead(2), Is.True);
            Assert.That(state.IsDead(3), Is.False);
            Assert.That(state.IsDead(5), Is.True);
        });
    }

    [Test]
    public void StateSize_Is64Bytes()
    {
        // Verify struct layout is correct for Redis serialization
        Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<LuckyMineState>(), Is.EqualTo(64));
    }
}
