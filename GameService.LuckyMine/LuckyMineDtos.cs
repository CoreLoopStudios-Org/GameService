namespace GameService.LuckyMine;

public record LuckyMineDto
{
    public ulong RevealedMask0 { get; init; }
    public ulong RevealedMask1 { get; init; }
    public int TotalTiles { get; init; }
    public int TotalMines { get; init; }
    public int RevealedSafeCount { get; init; }
    public int EntryCost { get; init; }
    public long CurrentWinnings { get; init; }
    public long NextTileWinnings { get; init; }
    public string Status { get; init; } = "Active";
}

public record LuckyMineFullDto : LuckyMineDto
{
    public ulong MineMask0 { get; init; }
    public ulong MineMask1 { get; init; }
}