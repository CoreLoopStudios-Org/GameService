using System.Text.Json.Serialization;
using GameService.ServiceDefaults.DTOs;

namespace GameService.ServiceDefaults.DTOs;

[JsonSerializable(typeof(PlayerUpdatedMessage))]
[JsonSerializable(typeof(PlayerChangeType))]
[JsonSerializable(typeof(PagedResult<PlayerUpdatedMessage>))]
public partial class ServiceDefaultsJsonContext : JsonSerializerContext
{
}
