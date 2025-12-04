using GameService.ServiceDefaults.Configuration;
using GameService.ServiceDefaults.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GameService.ApiService.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<GameServiceOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GameDbContext>>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        await db.Database.EnsureCreatedAsync();

        if (!await roleManager.RoleExistsAsync("Admin")) await roleManager.CreateAsync(new IdentityRole("Admin"));

        var adminEmail = options.AdminSeed.Email;
        var adminPassword = options.AdminSeed.Password;

        // Validate admin credentials are configured in production
        if (!env.IsDevelopment() && (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword)))
        {
            logger.LogWarning("Admin seed credentials not configured. Set GameService:AdminSeed:Email and GameService:AdminSeed:Password environment variables.");
            return;
        }

        // Use defaults only in development
        if (env.IsDevelopment())
        {
            if (string.IsNullOrEmpty(adminEmail)) adminEmail = "admin@gameservice.com";
            if (string.IsNullOrEmpty(adminPassword)) adminPassword = "AdminPass123!";
        }

        if (!string.IsNullOrEmpty(adminEmail) && await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                db.PlayerProfiles.Add(new PlayerProfile { UserId = admin.Id, Coins = options.AdminSeed.InitialCoins });
                await db.SaveChangesAsync();
                logger.LogInformation("Admin account created: {Email}", adminEmail);
            }
            else
            {
                logger.LogError("Failed to create admin account: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}