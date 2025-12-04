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

        await db.Database.EnsureCreatedAsync();

        if (!await roleManager.RoleExistsAsync("Admin")) await roleManager.CreateAsync(new IdentityRole("Admin"));

        var adminEmail = options.AdminSeed.Email;
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, options.AdminSeed.Password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                db.PlayerProfiles.Add(new PlayerProfile { UserId = admin.Id, Coins = options.AdminSeed.InitialCoins });
                await db.SaveChangesAsync();
            }
        }
    }
}