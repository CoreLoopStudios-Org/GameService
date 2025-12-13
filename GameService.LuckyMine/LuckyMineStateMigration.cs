using System.Runtime.InteropServices;
using GameService.GameCore;

namespace GameService.LuckyMine;

public class LuckyMineStateMigration : IStateMigration<LuckyMineState>
{
    public byte FromVersion => 1;
    public byte ToVersion => 1;
    public int FromSize => 56;

    public bool TryMigrate(ReadOnlySpan<byte> oldData, out LuckyMineState newState)
    {
        newState = new LuckyMineState();
        
        if (oldData.Length < 56) return false;
        
        newState.MineMask0 = MemoryMarshal.Read<ulong>(oldData.Slice(0, 8));
        newState.MineMask1 = MemoryMarshal.Read<ulong>(oldData.Slice(8, 8));
        newState.RevealedMask0 = MemoryMarshal.Read<ulong>(oldData.Slice(16, 8));
        newState.RevealedMask1 = MemoryMarshal.Read<ulong>(oldData.Slice(24, 8));
        
        newState.RevealedSafeCount = MemoryMarshal.Read<int>(oldData.Slice(32, 4));
        newState.TotalMines = oldData[36];
        newState.TotalTiles = oldData[37];
        newState.Status = oldData[38];
        
        // PendingPayout is new, default false.
        
        newState.EntryCost = MemoryMarshal.Read<int>(oldData.Slice(40, 4));
        newState.RewardSlope = MemoryMarshal.Read<float>(oldData.Slice(44, 4));
        newState.CurrentWinnings = MemoryMarshal.Read<long>(oldData.Slice(48, 8));
        
        return true;
    }
}
