using System.Security.Claims;
using System.Text.Json;
using GameService.ApiService;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.Messages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<GameDbContext>("postgresdb");
builder.AddRedisClient("cache");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, GameJsonContext.Default);
});

builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<GameDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    await db.Database.EnsureCreatedAsync();

    var adminEmail = "admin@gameservice.com";
    var adminPass = "AdminPass123!";

    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        var result = await userManager.CreateAsync(admin, adminPass);
        
        if (result.Succeeded)
        {
            db.PlayerProfiles.Add(new PlayerProfile { UserId = admin.Id, Coins = 1_000_000 });
            await db.SaveChangesAsync();
            Console.WriteLine($"✅ Admin Created: {adminEmail}");
        }
    }
}

app.UseHttpsRedirection();
app.MapGroup("/auth").MapIdentityApi<ApplicationUser>();

var gameGroup = app.MapGroup("/game").RequireAuthorization();

// 1. UPDATED /game/me Endpoint
gameGroup.MapGet("/me", async (HttpContext ctx, GameDbContext db, IConnectionMultiplexer redis) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (userId is null) return Results.Unauthorized();

    // Include User to get details if we need to publish
    var profile = await db.PlayerProfiles
        .Include(p => p.User)
        .FirstOrDefaultAsync(p => p.UserId == userId);

    if (profile is null)
    {
        // Create new profile
        profile = new PlayerProfile { UserId = userId, Coins = 100 };
        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();
        
        // Re-load user info for the message (or get from Claims)
        var user = await db.Users.FindAsync(userId);

        // --- LIVE UPDATE: Notify Dashboard of NEW User ---
        var message = new PlayerUpdatedMessage(userId, profile.Coins, user?.UserName, user?.Email);
        var json = JsonSerializer.Serialize(message, GameJsonContext.Default.PlayerUpdatedMessage);
        await redis.GetSubscriber().PublishAsync("player_updates", json);
        // ------------------------------------------------
    }
    
    return Results.Ok(new PlayerProfileResponse(profile.UserId, profile.Coins));
});

// 2. UPDATED Transaction Endpoint
gameGroup.MapPost("/coins/transaction", async (
    UpdateCoinRequest req, 
    HttpContext ctx, 
    GameDbContext db,
    IConnectionMultiplexer redis) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    // Include User so we can send Name/Email in the update
    var profile = await db.PlayerProfiles
        .Include(p => p.User)
        .FirstOrDefaultAsync(p => p.UserId == userId);
    
    if (profile is null)
    {
        profile = new PlayerProfile { UserId = userId!, Coins = 100 };
        db.PlayerProfiles.Add(profile);
        // Note: For perfect robustness, we should fetch User here too if strictly needed,
        // but usually the profile exists by the time transaction is called.
    }

    if (req.Amount < 0 && profile.Coins + req.Amount < 0) return Results.BadRequest("Insufficient funds");

    profile.Coins += req.Amount;
    profile.Version = Guid.NewGuid();
    await db.SaveChangesAsync();

    // --- LIVE UPDATE ---
    // Pass Username/Email so the dashboard can Upsert
    var message = new PlayerUpdatedMessage(
        profile.UserId, 
        profile.Coins, 
        profile.User?.UserName ?? "Unknown", 
        profile.User?.Email ?? "Unknown");

    var json = JsonSerializer.Serialize(message, GameJsonContext.Default.PlayerUpdatedMessage);
    await redis.GetSubscriber().PublishAsync("player_updates", json);
    // -------------------
    
    return Results.Ok(new { NewBalance = profile.Coins });
});

var adminGroup = app.MapGroup("/admin").RequireAuthorization();

adminGroup.MapGet("/users", async (GameDbContext db) =>
{
    var users = await db.PlayerProfiles
        .Include(p => p.User)
        .Select(p => new 
        { 
            Id = p.Id, 
            Username = p.User.UserName, 
            Email = p.User.Email 
        })
        .ToListAsync();

    return Results.Ok(users);
});

adminGroup.MapDelete("/users/{id}", async (int id, HttpContext ctx, GameDbContext db, UserManager<ApplicationUser> userManager) =>
{
    var currentUserId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    var profile = await db.PlayerProfiles
        .Include(p => p.User)
        .FirstOrDefaultAsync(p => p.Id == id);

    if (profile is null) return Results.NotFound();

    if (profile.UserId == currentUserId)
    {
        return Results.BadRequest("You cannot delete yourself.");
    }

    await userManager.DeleteAsync(profile.User);
    
    return Results.Ok();
});

app.MapDefaultEndpoints();
app.Run();