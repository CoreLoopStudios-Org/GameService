using System.Text.Json.Serialization;

namespace GameService.Ludo;

[JsonSerializable(typeof(LudoRoomMeta))]
[JsonSerializable(typeof(LudoContext))]
[JsonSerializable(typeof(List<LudoContext>))]
[JsonSerializable(typeof(LudoState))]
public partial class LudoJsonContext : JsonSerializerContext
{
}