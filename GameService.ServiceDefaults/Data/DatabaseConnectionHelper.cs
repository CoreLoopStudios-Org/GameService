using GameService.ServiceDefaults.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameService.ServiceDefaults.Data;

/// <summary>
///     Helper for configuring PostgreSQL connections with pooling and read replicas.
/// </summary>
public static class DatabaseConnectionHelper
{
    /// <summary>
    ///     Configures the primary DbContext with connection pooling settings.
    /// </summary>
    public static void ConfigureNpgsqlWithPooling(
        DbContextOptionsBuilder options,
        string connectionString,
        DatabaseOptions dbOptions)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = dbOptions.MaxPoolSize,
            MinPoolSize = dbOptions.MinPoolSize,
            ConnectionIdleLifetime = dbOptions.ConnectionIdleLifetime,
            Timeout = dbOptions.ConnectionTimeout,
            CommandTimeout = dbOptions.CommandTimeout,
            Pooling = dbOptions.Pooling
        };

        options.UseNpgsql(builder.ConnectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                3,
                TimeSpan.FromSeconds(5),
                null);

            npgsqlOptions.CommandTimeout(dbOptions.CommandTimeout);
        });
    }

    /// <summary>
    ///     Builds a connection string with pooling settings applied.
    /// </summary>
    public static string BuildConnectionString(string baseConnectionString, DatabaseOptions dbOptions)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            MaxPoolSize = dbOptions.MaxPoolSize,
            MinPoolSize = dbOptions.MinPoolSize,
            ConnectionIdleLifetime = dbOptions.ConnectionIdleLifetime,
            Timeout = dbOptions.ConnectionTimeout,
            CommandTimeout = dbOptions.CommandTimeout,
            Pooling = dbOptions.Pooling
        };

        return builder.ConnectionString;
    }
}

/// <summary>
///     Read-only DbContext for queries that can use a read replica.
///     Use this for player lookups, leaderboards, and history queries.
/// </summary>
public class ReadOnlyGameDbContext : GameDbContext
{
    public ReadOnlyGameDbContext(DbContextOptions<ReadOnlyGameDbContext> options)
        : base(ChangeOptionsType(options))
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    private static DbContextOptions<GameDbContext> ChangeOptionsType(DbContextOptions<ReadOnlyGameDbContext> options)
    {
        var builder = new DbContextOptionsBuilder<GameDbContext>();

        foreach (var extension in options.Extensions) builder.Options.WithExtension(extension);

        return builder.Options;
    }

    public override int SaveChanges()
    {
        throw new InvalidOperationException("This is a read-only context. Use the primary GameDbContext for writes.");
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("This is a read-only context. Use the primary GameDbContext for writes.");
    }
}

/// <summary>
///     Extension methods for registering database contexts with pooling.
/// </summary>
public static class DatabaseServiceExtensions
{
    /// <summary>
    ///     Adds a read-only DbContext that uses the read replica if configured.
    ///     Falls back to primary connection if no replica is configured.
    /// </summary>
    public static IServiceCollection AddReadOnlyDbContext(
        this IServiceCollection services,
        IConfiguration configuration,
        string primaryConnectionStringName = "postgresdb")
    {
        var gameOptions = configuration
            .GetSection(GameServiceOptions.SectionName)
            .Get<GameServiceOptions>() ?? new GameServiceOptions();

        var dbOptions = gameOptions.Database;

        var connectionString = !string.IsNullOrEmpty(dbOptions.ReadReplicaConnectionString)
            ? dbOptions.ReadReplicaConnectionString
            : configuration.GetConnectionString(primaryConnectionStringName);

        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException(
                $"Connection string '{primaryConnectionStringName}' not found and no ReadReplicaConnectionString configured.");

        services.AddDbContext<ReadOnlyGameDbContext>(options =>
        {
            DatabaseConnectionHelper.ConfigureNpgsqlWithPooling(options, connectionString, dbOptions);
        });

        return services;
    }
}