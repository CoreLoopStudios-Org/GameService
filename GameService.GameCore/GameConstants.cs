namespace GameService.GameCore;

public static class GameConstants
{
    public static class Actions
    {
        public const string Click = "click";
        public const string Roll = "roll";
        public const string Move = "move";
    }

    public static class GameTypes
    {
        public const string Ludo = "Ludo";
        public const string LuckyMine = "LuckyMine";
    }

    public static class Events
    {
        public const string GameEnded = "GameEnded";
        public const string PlayerUpdated = "PlayerUpdated";
    }

    public static class Economy
    {
        public const string Credit = "Credit";
        public const string Debit = "Debit";
    }
}
