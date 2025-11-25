using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GameService.ApiService;
using GameService.ApiService.Data;
using GameService.ApiService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<GameDbContext>("postgresdb");
builder.AddRedisOutputCache("cache");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, GameJsonContext.Default);
});

var jwtKey = "SuperSecretKeyThatShouldBeInUserSecretsOrKeyVault123!";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    // Note: If you get DB errors, delete the old volume!
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

// --- AUTH GROUP ---
var authGroup = app.MapGroup("/auth");

// 1. Register (FIXED: Returns Token)
authGroup.MapPost("/register", async (RegisterRequest req, GameDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Username == req.Username || u.Email == req.Email))
        return Results.Conflict("User already exists");

    var user = new User
    {
        Username = req.Username,
        Email = req.Email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Profile = new PlayerProfile 
        { 
            Coins = 100, 
            Stats = new Dictionary<string, object> { { "Level", 1 } },
            Version = Guid.NewGuid() // Explicit Init
        }
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    // Generate Tokens immediately
    var token = GenerateJwt(user, keyBytes);
    var refreshToken = GenerateRefreshToken();

    user.RefreshToken = refreshToken;
    user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
    await db.SaveChangesAsync();

    return Results.Ok(new LoginResponse(token, refreshToken, 3600));
});

// 2. Login
authGroup.MapPost("/login", async (LoginRequest req, GameDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    var token = GenerateJwt(user, keyBytes);
    var refreshToken = GenerateRefreshToken();

    user.RefreshToken = refreshToken;
    user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
    await db.SaveChangesAsync();

    return Results.Ok(new LoginResponse(token, refreshToken, 3600));
});

// 3. Refresh Token
authGroup.MapPost("/refresh", async (RefreshRequest req, GameDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.RefreshToken == req.RefreshToken);
    if (user is null || user.RefreshTokenExpiry < DateTime.UtcNow)
        return Results.Unauthorized();

    var newToken = GenerateJwt(user, keyBytes);
    var newRefreshToken = GenerateRefreshToken();

    user.RefreshToken = newRefreshToken;
    user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
    await db.SaveChangesAsync();

    return Results.Ok(new LoginResponse(newToken, newRefreshToken, 3600));
});

// 4. Logout
authGroup.MapPost("/logout", [Authorize] async (HttpContext ctx, GameDbContext db) =>
{
    var id = int.Parse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    var user = await db.Users.FindAsync(id);
    if (user != null)
    {
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await db.SaveChangesAsync();
    }
    return Results.Ok();
});

// 5. Change Password
authGroup.MapPost("/change-password", [Authorize] async (ChangePasswordRequest req, HttpContext ctx, GameDbContext db) =>
{
    var id = int.Parse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    var user = await db.Users.FindAsync(id);

    if (user is null || !BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
        return Results.BadRequest("Invalid old password");

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
    await db.SaveChangesAsync();
    return Results.Ok("Password changed");
});

// --- GAME GROUP ---
var gameGroup = app.MapGroup("/game").RequireAuthorization();

gameGroup.MapGet("/me", async (HttpContext ctx, GameDbContext db) =>
{
    var userId = int.Parse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    
    var profile = await db.PlayerProfiles
        .AsNoTracking()
        .Include(p => p.User)
        .FirstOrDefaultAsync(p => p.UserId == userId);

    if (profile is null) return Results.NotFound("Profile not found");

    return Results.Ok(new PlayerProfileResponse(
        profile.UserId, 
        profile.User.Username, 
        profile.Coins, 
        profile.Stats));
});

// Transaction Endpoint (FIXED: Concurrency Handling)
gameGroup.MapPost("/coins/transaction", async (UpdateCoinRequest req, HttpContext ctx, GameDbContext db) =>
{
    var userId = int.Parse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    
    var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
    if (profile is null) return Results.NotFound();

    if (req.Amount < 0 && profile.Coins + req.Amount < 0)
        return Results.BadRequest("Insufficient funds");

    profile.Coins += req.Amount;
    
    // Rotate the Version GUID to trigger Optimistic Concurrency Check
    profile.Version = Guid.NewGuid();

    try
    {
        await db.SaveChangesAsync();
        return Results.Ok(new { NewBalance = profile.Coins });
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict("Transaction failed due to concurrent update.");
    }
});

// --- ADMIN GROUP ---
var adminGroup = app.MapGroup("/admin");

adminGroup.MapGet("/users", async (GameDbContext db) =>
{
    return await db.Users
        .Select(u => new UserResponse(u.Id, u.Username, u.Email))
        .ToListAsync();
});

adminGroup.MapDelete("/users/{id}", async (int id, GameDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();
    
    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapDefaultEndpoints();
app.Run();

// Helpers
static string GenerateJwt(User user, byte[] key)
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

static string GenerateRefreshToken()
{
    var randomNumber = new byte[32];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(randomNumber);
    return Convert.ToBase64String(randomNumber);
}