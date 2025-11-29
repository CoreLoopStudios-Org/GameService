using GameService.ApiService;
using GameService.ApiService.Features.Auth;
using GameService.ApiService.Features.Economy;
using GameService.ApiService.Features.Players;
using GameService.ApiService.Hubs;
using GameService.ApiService.Infrastructure.Data;
using GameService.ServiceDefaults.Security;
using GameService.ServiceDefaults.Data;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

using GameService.ApiService.Features.Common;
using GameService.ApiService.Features.Admin;
using GameService.GameCore;
using GameService.Ludo; // Still needed for LudoJsonContext registration if we don't move it

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<GameDbContext>("postgresdb");
builder.AddRedisClient("cache");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, GameJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, LudoJsonContext.Default);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
});
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<GameDbContext>();

builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2PasswordHasher>();

builder.Services.AddScoped<IGameEventPublisher, RedisGameEventPublisher>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IEconomyService, EconomyService>();

// Auto-discover games
var modules = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
    .Select(Activator.CreateInstance)
    .Cast<IGameModule>()
    .ToList();

foreach (var module in modules)
{
    module.RegisterServices(builder.Services);
    builder.Services.AddSingleton(module);
}

builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("cache") ?? throw new InvalidOperationException("Redis connection string is missing"))
    .AddJsonProtocol();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
    await DbInitializer.InitializeAsync(app.Services);
}

// app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();

// API Key Middleware for Service-to-Service Admin Access
app.Use(async (context, next) =>
{
    var apiKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();
    var configuredKey = context.RequestServices.GetRequiredService<IConfiguration>()["AdminSettings:ApiKey"];

    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(configuredKey) && apiKey == configuredKey)
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin") };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "ApiKey");
        context.User = new System.Security.Claims.ClaimsPrincipal(identity);
    }
    await next();
});

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapPlayerEndpoints();
app.MapEconomyEndpoints();
app.MapAdminEndpoints();

app.MapHub<GameHub>("/hubs/game");

// Map game modules
foreach (var module in app.Services.GetServices<IGameModule>())
{
    module.MapEndpoints(app);
}

app.MapDefaultEndpoints();
app.Run();