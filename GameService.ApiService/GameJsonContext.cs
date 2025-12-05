using System.Text.Json.Serialization;
using GameService.ApiService.Hubs;
using GameService.GameCore;
using GameService.ServiceDefaults.DTOs;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace GameService.ApiService;

/// <summary>
///     API layer JSON serialization context.
///     Contains types used by the API endpoints, SignalR hub, and core infrastructure.
///     Game-specific types (LudoStateDto, LuckyMineDto) are in their own module contexts
///     for better AOT tree-shaking.
/// </summary>
// Core API types
[JsonSerializable(typeof(UpdateCoinRequest))]
[JsonSerializable(typeof(PlayerProfileResponse))]
[JsonSerializable(typeof(List<PlayerProfileResponse>))]
[JsonSerializable(typeof(AdminPlayerDto))]
[JsonSerializable(typeof(List<AdminPlayerDto>))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]

// Auth types
[JsonSerializable(typeof(AccessTokenResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterRequest))]

// Player update events
[JsonSerializable(typeof(PlayerUpdatedMessage))]
[JsonSerializable(typeof(PlayerChangeType))]

// Game catalog types
[JsonSerializable(typeof(SupportedGameDto))]
[JsonSerializable(typeof(List<SupportedGameDto>))]
[JsonSerializable(typeof(GameRoomDto))]
[JsonSerializable(typeof(List<GameRoomDto>))]

// Game core types
[JsonSerializable(typeof(GameRoomMeta))]
[JsonSerializable(typeof(GameStateResponse))]
[JsonSerializable(typeof(GameActionResult))]
[JsonSerializable(typeof(GameEvent))]
[JsonSerializable(typeof(List<GameEvent>))]
[JsonSerializable(typeof(JoinRoomResult))]

// SignalR hub response types
[JsonSerializable(typeof(CreateRoomResponse))]
[JsonSerializable(typeof(JoinRoomResponse))]
[JsonSerializable(typeof(SpectateRoomResponse))]

// SignalR strongly typed payloads
[JsonSerializable(typeof(PlayerJoinedPayload))]
[JsonSerializable(typeof(PlayerLeftPayload))]
[JsonSerializable(typeof(PlayerDisconnectedPayload))]
[JsonSerializable(typeof(PlayerReconnectedPayload))]
[JsonSerializable(typeof(ActionErrorPayload))]
[JsonSerializable(typeof(ChatMessagePayload))]
[JsonSerializable(typeof(GameEventPayload))]

// Legacy SignalR event types (for backwards compatibility)
[JsonSerializable(typeof(PlayerJoinedEvent))]
[JsonSerializable(typeof(PlayerLeftEvent))]
[JsonSerializable(typeof(ActionErrorEvent))]
[JsonSerializable(typeof(PlayerDisconnectedEvent))]
[JsonSerializable(typeof(PlayerReconnectedEvent))]
[JsonSerializable(typeof(ChatMessageEvent))]

// Outbox types
[JsonSerializable(typeof(GameEndedPayload))]

// Template types
[JsonSerializable(typeof(GameTemplateDto))]
[JsonSerializable(typeof(List<GameTemplateDto>))]
[JsonSerializable(typeof(CreateTemplateRequest))]
[JsonSerializable(typeof(CreateRoomFromTemplateRequest))]

// Generic dictionary types
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, DateTimeOffset>))]

// Economy types
[JsonSerializable(typeof(WalletTransactionDto))]
[JsonSerializable(typeof(List<WalletTransactionDto>))]
[JsonSerializable(typeof(PagedResult<WalletTransactionDto>))]

// Profile types
[JsonSerializable(typeof(UpdateProfileRequest))]

// Matchmaking types
[JsonSerializable(typeof(QuickMatchRequest))]
[JsonSerializable(typeof(QuickMatchResponse))]

// Leaderboard types
[JsonSerializable(typeof(LeaderboardEntryDto))]
[JsonSerializable(typeof(List<LeaderboardEntryDto>))]
internal partial class GameJsonContext : JsonSerializerContext
{
}