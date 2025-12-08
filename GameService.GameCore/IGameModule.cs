using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameService.GameCore;

public interface IGameModule
{
    string GameName { get; }

    Version Version => new(1, 0, 0);

    JsonSerializerContext? JsonContext => null;

    void RegisterServices(IServiceCollection services);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}