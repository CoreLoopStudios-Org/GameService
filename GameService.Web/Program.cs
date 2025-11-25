using GameService.ServiceDefaults.Data;
using GameService.Web;
using GameService.Web.Services;
using GameService.Web.Components;
using GameService.Web.Workers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore; // Required for EnsureCreatedAsync

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Redis
builder.AddRedisOutputCache("cache");
builder.AddRedisClient("cache"); 

// Services
builder.Services.AddSingleton<PlayerUpdateNotifier>();
builder.Services.AddHostedService<RedisLogStreamer>();

// DB
builder.AddNpgsqlDbContext<GameDbContext>("postgresdb");

// Identity
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<GameDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// --- DB MIGRATION FIX ---
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        // Fix: Ensure tables exist before UI tries to query them
        await db.Database.EnsureCreatedAsync();
    }

    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
// ------------------------

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseOutputCache();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();
app.MapDefaultEndpoints();

app.Run();