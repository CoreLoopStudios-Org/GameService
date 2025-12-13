using System.Text.Json.Serialization;
using GameService.ServiceDefaults.DTOs;

namespace GameService.ServiceDefaults.DTOs;

[JsonSerializable(typeof(PlayerUpdatedMessage))]
public partial class ServiceDefaultsJsonContext : JsonSerializerContext
{
}
