namespace GameService.ServiceDefaults.Configuration;

/// <summary>
/// Central configuration options for the GameService platform.
/// </summary>
public class GameServiceOptions
{
    public const string SectionName = "GameService";

    /// <summary>
    /// Economy-related settings (coins, transactions, etc.)
    /// </summary>
    public EconomyOptions Economy { get; set; } = new();

    /// <summary>
    /// Game session and connection settings
    /// </summary>
    public SessionOptions Session { get; set; } = new();

    /// <summary>
    /// Admin account seeding settings
    /// </summary>
    public AdminSeedOptions AdminSeed { get; set; } = new();

    /// <summary>
    /// Rate limiting settings
    /// </summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// CORS settings
    /// </summary>
    public CorsOptions Cors { get; set; } = new();

    /// <summary>
    /// Game loop worker settings
    /// </summary>
    public GameLoopOptions GameLoop { get; set; } = new();
}

public class EconomyOptions
{
    /// <summary>
    /// Initial coins given to new players
    /// </summary>
    public long InitialCoins { get; set; } = 100;
}

public class SessionOptions
{
    /// <summary>
    /// Grace period in seconds for player reconnection before being removed from game
    /// </summary>
    public int ReconnectionGracePeriodSeconds { get; set; } = 15;
}

public class AdminSeedOptions
{
    /// <summary>
    /// Admin account email for seeding
    /// </summary>
    public string Email { get; set; } = "admin@gameservice.com";

    /// <summary>
    /// Admin account password for seeding
    /// </summary>
    public string Password { get; set; } = "AdminPass123!";

    /// <summary>
    /// Initial coins for admin account
    /// </summary>
    public long InitialCoins { get; set; } = 1_000_000;
}

public class RateLimitOptions
{
    /// <summary>
    /// Maximum number of requests per window
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Rate limit window in minutes
    /// </summary>
    public int WindowMinutes { get; set; } = 1;
}

public class CorsOptions
{
    /// <summary>
    /// Allowed origins for CORS (comma-separated in production)
    /// </summary>
    public string[] AllowedOrigins { get; set; } = ["https://yourdomain.com"];
}

public class GameLoopOptions
{
    /// <summary>
    /// Interval in milliseconds between game loop ticks
    /// </summary>
    public int TickIntervalMs { get; set; } = 5000;
}
