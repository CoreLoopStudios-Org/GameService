using System.ComponentModel.DataAnnotations;
using GameService.ServiceDefaults.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameService.ServiceDefaults.Data;

public class GameDbContext(DbContextOptions<GameDbContext> options, IGameEventPublisher? publisher = null) 
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.HasOne(u => u.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<PlayerProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlayerProfile>()
            .HasIndex(p => p.UserId)
            .IsUnique();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var newUsers = ChangeTracker.Entries<ApplicationUser>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        foreach (var user in newUsers)
        {
            var hasProfile = ChangeTracker.Entries<PlayerProfile>()
                .Any(e => e.State == EntityState.Added && e.Entity.User == user);

            if (!hasProfile && user.Profile == null)
            {
                PlayerProfiles.Add(new PlayerProfile
                {
                    User = user,
                    UserId = user.Id,
                    Coins = 100,
                    Version = Guid.NewGuid()
                });
            }
        }

        var addedProfiles = new List<PlayerProfile>();
        if (publisher != null)
        {
            addedProfiles = ChangeTracker.Entries<PlayerProfile>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        if (result > 0 && addedProfiles.Count > 0 && publisher != null)
        {
            foreach (var profile in addedProfiles)
            {
                var username = profile.User?.UserName ?? "New Player";
                var email = profile.User?.Email ?? "Unknown";

                var message = new PlayerUpdatedMessage(
                    profile.UserId,
                    profile.Coins,
                    username,
                    email,
                    PlayerChangeType.Updated,
                    profile.Id);

                _ = Task.Run(() => publisher.PublishPlayerUpdatedAsync(message), cancellationToken);
            }
        }

        return result;
    }
}

public class ApplicationUser : IdentityUser
{
    public PlayerProfile? Profile { get; set; }
}

public class PlayerProfile
{
    public int Id { get; set; }

    [MaxLength(450)] public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public long Coins { get; set; } = 100;

    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}