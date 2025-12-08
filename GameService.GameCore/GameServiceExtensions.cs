using Microsoft.Extensions.DependencyInjection;

namespace GameService.GameCore;

public static class GameServiceExtensions
{
    public static IServiceCollection AddGamePlatform(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddGameModule<TModule>(this IServiceCollection services)
        where TModule : IGameModule, new()
    {
        var module = new TModule();

        services.AddSingleton<IGameModule>(module);

        module.RegisterServices(services);

        return services;
    }

    public static IServiceCollection AddKeyedGameEngine<TEngine>(
        this IServiceCollection services,
        string gameType)
        where TEngine : class, IGameEngine
    {
        services.AddKeyedSingleton<IGameEngine, TEngine>(gameType);
        return services;
    }

    public static IServiceCollection AddKeyedGameRoomService<TService>(
        this IServiceCollection services,
        string gameType)
        where TService : class, IGameRoomService
    {
        services.AddKeyedSingleton<IGameRoomService, TService>(gameType);
        return services;
    }
}