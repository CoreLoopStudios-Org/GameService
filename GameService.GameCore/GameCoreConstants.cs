namespace GameService.GameCore;

/// <summary>
///     Constants used across game engines for consistency.
/// </summary>
public static class GameCoreConstants
{
    /// <summary>
    ///     User ID representing admin-initiated actions
    /// </summary>
    public const string AdminUserId = "ADMIN";

    /// <summary>
    ///     User ID representing system-initiated actions (e.g., timeouts, auto-play)
    /// </summary>
    public const string SystemUserId = "SYSTEM";
}