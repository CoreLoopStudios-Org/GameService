using System.Security.Claims;
using System.Text.Json;
using GameService.ApiService;
using GameService.ServiceDefaults.Data;
using GameService.ServiceDefaults.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<GameDbContext>("postgresdb");
builder.AddRedisClient("cache");

// Optimization: Use Source Generators for JSON
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, GameJsonContext.Default);
});

builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<GameDbContext>();

var app = builder.Build();

// Development Seeding
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // Safe migration for Dev
    await db.Database.EnsureCreatedAsync();

    const string adminEmail = "admin@gameservice.com";
    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        var result = await userManager.CreateAsync(admin, "AdminPass123!");
        if (result.Succeeded)
        {
            db.PlayerProfiles.Add(new PlayerProfile { UserId = admin.Id, Coins = 1_000_000 });
            await db.SaveChangesAsync();
        }
    }
}

app.UseHttpsRedirection();
app.MapGroup("/auth").MapIdentityApi<ApplicationUser>();

var gameGroup = app.MapGroup("/game").RequireAuthorization();

// 1. GET Profile
gameGroup.MapGet("/me", async (HttpContext ctx, GameDbContext db, IConnectionMultiplexer redis) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var profile = await db.PlayerProfiles
        .AsNoTracking() // Optimization: Read-only query
        .FirstOrDefaultAsync(p => p.UserId == userId);

    if (profile is null)
    {
        // Double-check creation logic. Ideally handled at registration, but lazy-load is acceptable.
        var newProfile = new PlayerProfile { UserId = userId, Coins = 100 };
        db.PlayerProfiles.Add(newProfile);
        try 
        {
            await db.SaveChangesAsync();
            profile = newProfile;
            
            // Fire & Forget notification
            var user = await db.Users.FindAsync(userId);
            var message = new PlayerUpdatedMessage(userId, profile.Coins, user?.UserName, user?.Email);
            var json = JsonSerializer.Serialize(message, GameJsonContext.Default.PlayerUpdatedMessage);
            await redis.GetSubscriber().PublishAsync("player_updates", json);
        }
        catch (DbUpdateException) 
        {
            // Handle race condition if created in parallel
            profile = await db.PlayerProfiles.AsNoTracking().FirstAsync(p => p.UserId == userId);
        }
    }
    
    return Results.Ok(new PlayerProfileResponse(profile.UserId, profile.Coins));
});

// 2. TRANSACTION Endpoint
gameGroup.MapPost("/coins/transaction", async (
    [FromBody] UpdateCoinRequest req, 
    HttpContext ctx, 
    GameDbContext db,
    IConnectionMultiplexer redis) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    // Validations
    if (req.Amount == 0) return Results.BadRequest("Amount cannot be zero");

    // Execution Strategy for Retries (Crucial for Cloud/Containers)
    var strategy = db.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Load with pessimistic lock for absolute safety during transaction
            // OR use Optimistic Concurrency. Here we use EF Core Optimistic via 'Version' property.
            var profile = await db.PlayerProfiles
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile is null)
            {
                // Lazy create (Should ideally be a separate service method)
                profile = new PlayerProfile { UserId = userId, Coins = 100 };
                db.PlayerProfiles.Add(profile);
            }

            // Logic Check
            if (req.Amount < 0 && (profile.Coins + req.Amount < 0))
            {
                return Results.BadRequest("Insufficient funds");
            }

            profile.Coins += req.Amount;
            profile.Version = Guid.NewGuid(); // Rotate version

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Post-commit: Notify
            var message = new PlayerUpdatedMessage(
                profile.UserId, 
                profile.Coins, 
                profile.User?.UserName ?? "Unknown", 
                profile.User?.Email ?? "Unknown");

            var json = JsonSerializer.Serialize(message, GameJsonContext.Default.PlayerUpdatedMessage);
            await redis.GetSubscriber().PublishAsync("player_updates", json);

            return Results.Ok(new { NewBalance = profile.Coins });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict("Transaction failed due to concurrent modification. Please retry.");
        }
    });
});

// Removed: Admin endpoints from API. 
// Rationale: Admin logic belongs in the Admin API or the Web App directly if it has DB access.
// Mixing user traffic and admin management in the same high-throughput API is bad design.
// If explicitly needed, they should be behind a strict policy ("RequireRole(Admin)").

app.MapDefaultEndpoints();
app.Run();