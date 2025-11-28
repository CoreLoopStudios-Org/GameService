using System.Text.Json;
using StackExchange.Redis;

namespace GameService.Ludo;

public class LudoRoomService(IConnectionMultiplexer redis)
{
    private readonly IDatabase _db = redis.GetDatabase();
    
    private string GetMetaKey(string roomId) => $"ludo:{roomId}:meta";
    private string GetStateKey(string roomId) => $"ludo:{roomId}:state";
    
    public async Task<string?> CreateRoomAsync(string hostUserId)
    {
        string roomId = Guid.NewGuid().ToString("N")[..8];

        var engine = new LudoEngine(new ServerDiceRoller());
        engine.InitNewGame(2);

        byte[] stateBytes = SerializeState(engine.State);
        await _db.StringSetAsync(GetStateKey(roomId), stateBytes);

        var meta = new LudoRoomMeta { 
            PlayerSeats = new() { { hostUserId, 0 } },
            IsPublic = true 
        };
        await _db.StringSetAsync(GetMetaKey(roomId), JsonSerializer.Serialize(meta));
        
        return roomId;
    }
    
    public async Task<bool> JoinRoomAsync(string roomId, string userId)
    {
        var metaKey = GetMetaKey(roomId);
        var json = await _db.StringGetAsync(metaKey);
        if (json.IsNullOrEmpty) return false;

        var meta = JsonSerializer.Deserialize<LudoRoomMeta>((string)json!);
        if (meta.PlayerSeats.ContainsKey(userId)) return true;
        if (meta.PlayerSeats.Count >= 2) return false;
        
        meta.PlayerSeats[userId] = 2;
        await _db.StringSetAsync(metaKey, JsonSerializer.Serialize(meta));
        
        return true;
    }
    
    public async Task<LudoContext?> LoadGameAsync(string roomId)
    {
        var stateBytes = await _db.StringGetAsync(GetStateKey(roomId));
        var metaJson = await _db.StringGetAsync(GetMetaKey(roomId));
        
        if (stateBytes.IsNullOrEmpty || metaJson.IsNullOrEmpty) return null;

        LudoState state = DeserializeState((byte[])stateBytes!);
        var meta = JsonSerializer.Deserialize<LudoRoomMeta>((string)metaJson!);
        
        return new LudoContext(roomId, new LudoEngine(state, new ServerDiceRoller()), meta!);
    }
    
    public async Task SaveGameAsync(LudoContext ctx)
    {
        byte[] stateBytes = SerializeState(ctx.Engine.State);
        await _db.StringSetAsync(GetStateKey(ctx.RoomId), stateBytes);
    }

    private static unsafe byte[] SerializeState(LudoState state)
    {
        var bytes = new byte[sizeof(LudoState)];
        fixed (byte* b = bytes) 
        { 
            *(LudoState*)b = state; 
        }
        return bytes;
    }
    
    private static unsafe LudoState DeserializeState(byte[] bytes)
    {
        fixed (byte* ptr = bytes) 
        { 
            return *(LudoState*)ptr; 
        }
    }
}

public record LudoRoomMeta
{
    public Dictionary<string, int> PlayerSeats { get; set; } = new();
    public bool IsPublic { get; set; }
}

public record LudoContext(string RoomId, LudoEngine Engine, LudoRoomMeta Meta);