namespace GameService.GameCore;

public interface IGameEngine
{
    void InitNewGame(int playerCount);
    // Add other common engine methods if necessary, or keep it marker for now
    // Ideally:
    // bool TryRollDice(...);
    // bool TryMoveToken(...);
    // But games vary wildly. For now, we keep it simple for the registry.
}