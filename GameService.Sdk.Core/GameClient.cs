using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

namespace GameService.Sdk.Core;

public sealed class GameClient : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly string _baseUrl;
    private bool _disposed;

    public ConnectionState State => _hub.State switch
    {
        HubConnectionState.Connected => ConnectionState.Connected,
        HubConnectionState.Connecting => ConnectionState.Connecting,
        HubConnectionState.Reconnecting => ConnectionState.Reconnecting,
        _ => ConnectionState.Disconnected
    };

    public string? CurrentRoomId { get; private set; }

    public int LatencyMs { get; private set; }
    public event Action<int>? OnLatencyUpdate;

    public event Action<GameState>? OnGameState;

    public event Action<PlayerJoined>? OnPlayerJoined;

    public event Action<PlayerLeft>? OnPlayerLeft;

    public event Action<PlayerDisconnected>? OnPlayerDisconnected;

    public event Action<PlayerReconnected>? OnPlayerReconnected;

    public event Action<ChatMessage>? OnChatMessage;

    public event Action<GameEvent>? OnGameEvent;

    public event Action<ActionError>? OnActionError;

    public event Action<ConnectionState>? OnConnectionStateChanged;

    private GameClient(HubConnection hub, string baseUrl)
    {
        _hub = hub;
        _baseUrl = baseUrl;
        SetupEventHandlers();
        _hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
    }

    public static async Task<GameClient> ConnectAsync(
        string baseUrl,
        Func<Task<string?>> accessTokenProvider,
        CancellationToken cancellationToken = default)
    {
        var hubUrl = baseUrl.TrimEnd('/') + "/hubs/game";

        var hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = accessTokenProvider;
            })
            .WithAutomaticReconnect(new RetryPolicy())
            .Build();

        var client = new GameClient(hub, baseUrl);
        await hub.StartAsync(cancellationToken);
        return client;
    }

    public static Task<GameClient> ConnectAsync(
        string baseUrl,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        return ConnectAsync(baseUrl, () => Task.FromResult<string?>(accessToken), cancellationToken);
    }

    public static GameClientBuilder Create(string baseUrl) => new(baseUrl);

    public async Task<int> PingAsync()
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();
        
        if (CurrentRoomId != null)
        {
            await _hub.InvokeAsync("GetSpectatorCount", CurrentRoomId);
        }
        else
        {
             using var http = new HttpClient();
             await http.GetAsync(_baseUrl + "/alive");
        }
        
        sw.Stop();
        LatencyMs = (int)sw.ElapsedMilliseconds;
        OnLatencyUpdate?.Invoke(LatencyMs);
        return LatencyMs;
    }

    public async Task<CreateRoomResult> CreateRoomAsync(string templateName)
    {
        EnsureConnected();
        var response = await _hub.InvokeAsync<CreateRoomResponse>("CreateRoom", templateName);
        
        if (response.Success && response.RoomId != null)
        {
            CurrentRoomId = response.RoomId;
        }
        
        return new CreateRoomResult(response.Success, response.RoomId, response.ErrorMessage);
    }

    public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
    {
        EnsureConnected();
        var response = await _hub.InvokeAsync<JoinRoomResponse>("JoinRoom", roomId);
        
        if (response.Success)
        {
            CurrentRoomId = roomId;
        }
        
        return new JoinRoomResult(response.Success, response.SeatIndex, response.ErrorMessage);
    }

    public async Task LeaveRoomAsync()
    {
        if (CurrentRoomId == null) return;
        EnsureConnected();
        
        await _hub.InvokeAsync("LeaveRoom", CurrentRoomId);
        CurrentRoomId = null;
    }

    public async Task<SpectateResult> SpectateAsync(string roomId)
    {
        EnsureConnected();
        var response = await _hub.InvokeAsync<SpectateRoomResponse>("SpectateRoom", roomId);
        return new SpectateResult(response.Success, response.ErrorMessage);
    }

    public async Task StopSpectatingAsync(string roomId)
    {
        EnsureConnected();
        await _hub.InvokeAsync("StopSpectating", roomId);
    }

    public async Task<ActionResult> PerformActionAsync(
        string actionName,
        object? payload = null,
        string? commandId = null)
    {
        EnsureConnected();
        if (CurrentRoomId == null)
            return new ActionResult(false, "Not in a room", null);

        var jsonPayload = payload == null 
            ? JsonDocument.Parse("{}").RootElement 
            : JsonSerializer.SerializeToElement(payload);

        var response = await _hub.InvokeAsync<GameActionResponse>(
            "PerformAction", 
            CurrentRoomId, 
            actionName, 
            jsonPayload,
            commandId);

        return new ActionResult(response.Success, response.ErrorMessage, response.NewState);
    }

    public async Task<IReadOnlyList<string>> GetLegalActionsAsync()
    {
        if (CurrentRoomId == null) return Array.Empty<string>();
        EnsureConnected();
        
        return await _hub.InvokeAsync<IReadOnlyList<string>>("GetLegalActions", CurrentRoomId);
    }

    public async Task<GameState?> GetStateAsync()
    {
        if (CurrentRoomId == null) return null;
        EnsureConnected();
        
        var response = await _hub.InvokeAsync<GameStateResponse?>("GetState", CurrentRoomId);
        return response == null ? null : MapGameState(response);
    }

    public async Task SendChatAsync(string message)
    {
        if (CurrentRoomId == null) return;
        EnsureConnected();
        
        await _hub.InvokeAsync("SendChatMessage", CurrentRoomId, message);
    }

    private void SetupEventHandlers()
    {
        _hub.On<GameStateResponse>("GameState", response =>
        {
            OnGameState?.Invoke(MapGameState(response));
        });

        _hub.On<PlayerJoinedPayload>("PlayerJoined", payload =>
        {
            OnPlayerJoined?.Invoke(new PlayerJoined(payload.UserId, payload.UserName, payload.SeatIndex));
        });

        _hub.On<PlayerLeftPayload>("PlayerLeft", payload =>
        {
            OnPlayerLeft?.Invoke(new PlayerLeft(payload.UserId, payload.UserName));
        });

        _hub.On<PlayerDisconnectedPayload>("PlayerDisconnected", payload =>
        {
            OnPlayerDisconnected?.Invoke(new PlayerDisconnected(
                payload.UserId, payload.UserName, payload.GracePeriodSeconds));
        });

        _hub.On<PlayerReconnectedPayload>("PlayerReconnected", payload =>
        {
            OnPlayerReconnected?.Invoke(new PlayerReconnected(payload.UserId, payload.UserName));
        });

        _hub.On<ChatMessagePayload>("ChatMessage", payload =>
        {
            OnChatMessage?.Invoke(new ChatMessage(
                payload.UserId, payload.UserName, payload.Message, payload.Timestamp));
        });

        _hub.On<GameEventPayload>("GameEvent", payload =>
        {
            OnGameEvent?.Invoke(new GameEvent(payload.EventName, payload.Data, payload.Timestamp));
        });

        _hub.On<ActionErrorPayload>("ActionError", payload =>
        {
            OnActionError?.Invoke(new ActionError(payload.Action, payload.ErrorMessage));
        });

        _hub.Reconnecting += _ =>
        {
            OnConnectionStateChanged?.Invoke(ConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        _hub.Reconnected += _ =>
        {
            OnConnectionStateChanged?.Invoke(ConnectionState.Connected);
            return Task.CompletedTask;
        };

        _hub.Closed += _ =>
        {
            OnConnectionStateChanged?.Invoke(ConnectionState.Disconnected);
            return Task.CompletedTask;
        };
    }

    private static GameState MapGameState(GameStateResponse response) => new(
        response.RoomId,
        response.GameType,
        response.Phase,
        response.CurrentTurnUserId,
        response.Meta.CurrentPlayerCount,
        response.Meta.MaxPlayers,
        response.Meta.PlayerSeats,
        response.GameSpecificState);

    private void EnsureConnected()
    {
        if (_hub.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Not connected to server");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_hub.State == HubConnectionState.Connected)
        {
            await _hub.StopAsync();
        }
        await _hub.DisposeAsync();
    }

    private class RetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays = 
        {
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            return retryContext.PreviousRetryCount < Delays.Length 
                ? Delays[retryContext.PreviousRetryCount] 
                : TimeSpan.FromSeconds(60);
        }
    }
}

public sealed class GameClientBuilder
{
    private readonly string _baseUrl;
    private Func<Task<string?>>? _tokenProvider;
    private Action<HubConnectionBuilder>? _configure;

    internal GameClientBuilder(string baseUrl) => _baseUrl = baseUrl;

    public GameClientBuilder WithAccessTokenProvider(Func<Task<string?>> provider)
    {
        _tokenProvider = provider;
        return this;
    }

    public GameClientBuilder WithAccessToken(string token)
    {
        _tokenProvider = () => Task.FromResult<string?>(token);
        return this;
    }

    public GameClientBuilder Configure(Action<HubConnectionBuilder> configure)
    {
        _configure = configure;
        return this;
    }

    public Task<GameClient> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenProvider == null)
            throw new InvalidOperationException("Access token provider is required. Call WithAccessToken() or WithAccessTokenProvider() first.");
        
        return GameClient.ConnectAsync(_baseUrl, _tokenProvider, cancellationToken);
    }
}
