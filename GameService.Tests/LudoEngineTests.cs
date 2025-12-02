using GameService.Ludo;

namespace GameService.Tests;

/// <summary>
///     Unit tests for the Ludo game engine core logic
/// </summary>
[TestFixture]
public class LudoEngineTests
{
    [SetUp]
    public void Setup()
    {
        _diceRoller = new TestDiceRoller();
        _engine = new LudoEngine(_diceRoller);
        _engine.InitNewGame(4);
    }

    private LudoEngine _engine = null!;
    private TestDiceRoller _diceRoller = null!;

    [Test]
    public void InitNewGame_SetsCorrectInitialState()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_engine.State.CurrentPlayer, Is.EqualTo(0));
            Assert.That(_engine.State.LastDiceRoll, Is.EqualTo(0));
            Assert.That(_engine.State.Winner, Is.EqualTo(255));
            Assert.That(_engine.State.TurnId, Is.EqualTo(1));
            Assert.That(_engine.State.ActiveSeats, Is.EqualTo(0b00001111));
        });
    }

    [Test]
    public void InitNewGame_TwoPlayers_SetsCorrectActiveSeats()
    {
        _engine.InitNewGame(2);

        Assert.That(_engine.State.ActiveSeats, Is.EqualTo(0b00000101));
    }

    [Test]
    public void InitNewGame_AllTokensInBase()
    {
        for (var player = 0; player < 4; player++)
        for (var token = 0; token < 4; token++)
            Assert.That(_engine.State.GetTokenPos(player, token), Is.EqualTo(LudoConstants.PosBase));
    }

    [Test]
    public void TryRollDice_ReturnsCorrectValue()
    {
        _diceRoller.NextRoll = 5;

        var success = _engine.TryRollDice(out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.DiceValue, Is.EqualTo(5));
            Assert.That(result.Status, Is.EqualTo(LudoStatus.TurnPassed));
        });
    }

    [Test]
    public void TryRollDice_WithMovablePiece_KeepsDiceValue()
    {
        _engine.State.SetTokenPos(0, 0, 10);

        _diceRoller.NextRoll = 3;
        var success = _engine.TryRollDice(out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.DiceValue, Is.EqualTo(3));
            Assert.That(_engine.State.LastDiceRoll, Is.EqualTo(3));
        });
    }

    [Test]
    public void TryRollDice_CannotRollWhenMoveRequired()
    {
        _engine.State.SetTokenPos(0, 0, 10);
        _diceRoller.NextRoll = 3;
        _engine.TryRollDice(out _);

        var success = _engine.TryRollDice(out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(result.Status, Is.EqualTo(LudoStatus.ErrorNeedToRoll));
        });
    }

    [Test]
    public void TryRollDice_Six_AllowsExtraTurn()
    {
        _diceRoller.NextRoll = 6;

        _engine.TryRollDice(out var result);

        Assert.That(result.Status, Is.EqualTo(LudoStatus.Success));
        Assert.That(_engine.State.ConsecutiveSixes, Is.EqualTo(1));
    }

    [Test]
    public void TryRollDice_ThreeSixes_ForfeitsTurn()
    {
        _diceRoller.NextRoll = 6;
        _engine.TryRollDice(out _);
        _engine.State.LastDiceRoll = 0;

        _engine.TryRollDice(out _);
        _engine.State.LastDiceRoll = 0;

        var success = _engine.TryRollDice(out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.Status, Is.EqualTo(LudoStatus.ForfeitTurn));
            Assert.That(_engine.State.CurrentPlayer, Is.EqualTo(1));
            Assert.That(_engine.State.ConsecutiveSixes, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryMoveToken_FromBase_RequiresSix()
    {
        _engine.State.SetTokenPos(0, 0, 10);
        _diceRoller.NextRoll = 5;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(1, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(result.Status, Is.EqualTo(LudoStatus.ErrorTokenInBase));
        });
    }

    [Test]
    public void TryMoveToken_FromBase_WithSix_MovesToStart()
    {
        _diceRoller.NextRoll = 6;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(0, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.Status.HasFlag(LudoStatus.Success), Is.True);
            Assert.That(result.NewPos, Is.EqualTo(LudoConstants.PosStart));
            Assert.That(_engine.State.GetTokenPos(0, 0), Is.EqualTo(LudoConstants.PosStart));
        });
    }

    [Test]
    public void TryMoveToken_AdvancesPosition()
    {
        _engine.State.SetTokenPos(0, 0, LudoConstants.PosStart);

        _diceRoller.NextRoll = 4;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(0, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.NewPos, Is.EqualTo(LudoConstants.PosStart + 4));
        });
    }

    [Test]
    public void TryMoveToken_CannotExceedHome()
    {
        _engine.State.SetTokenPos(0, 0, 55);

        _diceRoller.NextRoll = 5;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(0, out var result);

        Assert.That(success, Is.False);
    }

    [Test]
    public void TryMoveToken_CapturesOpponent()
    {
        _engine.State.SetTokenPos(0, 0, 10);
        _engine.State.SetTokenPos(1, 0, 14);

        _diceRoller.NextRoll = 4;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(0, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.Status.HasFlag(LudoStatus.CapturedOpponent), Is.True);
            Assert.That(result.CapturedPid, Is.EqualTo(1));
            Assert.That(result.CapturedTid, Is.EqualTo(0));
            Assert.That(_engine.State.GetTokenPos(1, 0), Is.EqualTo(LudoConstants.PosBase));
        });
    }

    [Test]
    public void TryMoveToken_Capture_GrantsExtraTurn()
    {
        _engine.State.SetTokenPos(0, 0, 10);
        _engine.State.SetTokenPos(1, 0, 14);

        _diceRoller.NextRoll = 4;
        _engine.TryRollDice(out _);
        _engine.TryMoveToken(0, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status.HasFlag(LudoStatus.ExtraTurn), Is.True);
            Assert.That(_engine.State.CurrentPlayer, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryMoveToken_AllTokensHome_WinsGame()
    {
        _engine.State.SetTokenPos(0, 0, LudoConstants.PosHome);
        _engine.State.SetTokenPos(0, 1, LudoConstants.PosHome);
        _engine.State.SetTokenPos(0, 2, LudoConstants.PosHome);
        _engine.State.SetTokenPos(0, 3, 55);

        _diceRoller.NextRoll = 2;
        _engine.TryRollDice(out _);

        var success = _engine.TryMoveToken(3, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(result.Status.HasFlag(LudoStatus.GameWon), Is.True);
            Assert.That(_engine.State.Winner, Is.EqualTo(0));
        });
    }

    [Test]
    public void GetLegalMoves_NoMovesWhenDiceNotRolled()
    {
        var moves = _engine.GetLegalMoves();

        Assert.That(moves, Is.Empty);
    }

    [Test]
    public void GetLegalMoves_OnlySixCanMoveFromBase()
    {
        _diceRoller.NextRoll = 5;
        _engine.TryRollDice(out _);

        var moves = _engine.GetLegalMoves();

        Assert.That(moves, Is.Empty);
    }

    [Test]
    public void GetLegalMoves_SixShowsAllBaseTokens()
    {
        _diceRoller.NextRoll = 6;
        _engine.TryRollDice(out _);

        var moves = _engine.GetLegalMoves();

        Assert.That(moves, Has.Count.EqualTo(4));
    }

    [Test]
    public void TurnAdvances_AfterNonSixMove()
    {
        _engine.State.SetTokenPos(0, 0, 10);

        _diceRoller.NextRoll = 3;
        _engine.TryRollDice(out _);
        _engine.TryMoveToken(0, out _);

        Assert.That(_engine.State.CurrentPlayer, Is.EqualTo(1));
    }

    [Test]
    public void TurnDoesNotAdvance_AfterSixMove()
    {
        _diceRoller.NextRoll = 6;
        _engine.TryRollDice(out _);
        _engine.TryMoveToken(0, out _);

        Assert.That(_engine.State.CurrentPlayer, Is.EqualTo(0));
    }
}

/// <summary>
///     Test dice roller that returns predetermined values
/// </summary>
public class TestDiceRoller : IDiceRoller
{
    public byte NextRoll { get; set; } = 1;

    public byte Roll()
    {
        return NextRoll;
    }
}