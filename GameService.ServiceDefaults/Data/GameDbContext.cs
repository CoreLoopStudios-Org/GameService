using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameService.ServiceDefaults.Data;

public class GameDbContext(DbContextOptions<GameDbContext> options) 
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

        // Performance: Index on UserId for fast lookups
        builder.Entity<PlayerProfile>()
            .HasIndex(p => p.UserId)
            .IsUnique();
    }
}

public class ApplicationUser : IdentityUser
{
    public PlayerProfile? Profile { get; set; }
}

public class PlayerProfile
{
    public int Id { get; set; }

    [MaxLength(450)] // Match Identity User Id length
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public long Coins { get; set; } = 100;

    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}