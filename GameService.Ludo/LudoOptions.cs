namespace GameService.Ludo;

/// <summary>
/// Ludo-specific configuration options.
/// Each game module owns its own options class.
/// </summary>
public class LudoOptions
{
    public const string SectionName = "Ludo";

    /// <summary>
    /// Turn timeout in seconds before auto-play kicks in
    /// </summary>
    public int TurnTimeoutSeconds { get; set; } = 30;
}
