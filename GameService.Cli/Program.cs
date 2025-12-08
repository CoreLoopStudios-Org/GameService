using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameService.Sdk.Auth;
using GameService.Sdk.Core;
using GameService.Sdk.LuckyMine;
using GameService.Sdk.Ludo;

// ==========================================
// CONFIGURATION
// ==========================================
const string BaseUrl = "http://localhost:5525";
Console.OutputEncoding = Encoding.UTF8;
Console.Title = "GameService CLI";

// ==========================================
// 1. AUTO-AUTH & BOOTSTRAP
// ==========================================
WriteHeader("INITIALIZING");

// We need a dedicated HTTP client for Catalog/Matchmaking endpoints
using var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
using var authClient = new AuthClient(BaseUrl, httpClient);

// Generate random credentials
var randomId = new Random().Next(1000, 9999);
var email = $"cli_{randomId}@test.local";
var password = "Password123!";

Console.Write($"[-] Registering {email}... ");
var regResult = await authClient.RegisterAsync(email, password);
if (!regResult.Success) { PrintError(regResult.Error!); return; }
PrintSuccess();

Console.Write($"[-] Logging in... ");
var loginResult = await authClient.LoginAsync(email, password);
if (!loginResult.Success) { PrintError(loginResult.Error!); return; }
PrintSuccess();

var session = loginResult.Session!;
var token = session.AccessToken;
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var profile = await session.GetProfileAsync();
Console.WriteLine($"[-] Welcome, {profile?.UserName}! Balance: {profile?.Coins:N0} coins");
Console.WriteLine();

// ==========================================
// 2. MAIN MENU
// ==========================================
while (true)
{
    Console.WriteLine("GAME SELECTOR:");
    Console.WriteLine("1. Play Ludo (Multiplayer Matchmaking)");
    Console.WriteLine("2. Play Lucky Mine (Single Player)");
    Console.WriteLine("3. Exit");
    Console.Write("> ");
    
    var choice = Console.ReadLine();
    try
    {
        Console.WriteLine("[-] Connecting to Game Gateway...");
        await using var gameClient = await GameClient.ConnectAsync(BaseUrl, token);
        Console.WriteLine("[-] Connected.");

        switch (choice)
        {
            case "1": await RunLudoFlow(httpClient, gameClient); break;
            case "2": await RunLuckyMineFlow(gameClient); break;
            case "3": return;
            default: Console.WriteLine("Invalid selection."); break;
        }
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        Console.WriteLine("Press any key to retry...");
        Console.ReadKey();
    }
    Console.Clear();
}

// ==========================================
// 3. LUDO FLOW (Matchmaking -> Lobby -> Game)
// ==========================================
async Task RunLudoFlow(HttpClient http, GameClient socket)
{
    var client = new LudoClient(socket);

    // 1. Matchmaking (HTTP)
    WriteHeader("LUDO MATCHMAKING");
    Console.WriteLine("Looking for an available room...");

    // We use QuickMatch to find a template-based room or create one automatically
    var matchReq = new { GameType = "Ludo", MaxPlayers = 4, EntryFee = 0 };
    var matchRes = await http.PostAsJsonAsync("/games/quick-match", matchReq);
    
    if (!matchRes.IsSuccessStatusCode)
    {
        PrintError($"Matchmaking failed: {matchRes.StatusCode}");
        return;
    }

    var matchData = await matchRes.Content.ReadFromJsonAsync<JsonElement>();
    var roomId = matchData.GetProperty("roomId").GetString()!;
    var action = matchData.GetProperty("action").GetString()!;

    Console.WriteLine($"[-] {action} Room: {roomId}");

    // 2. Join SignalR Group
    var joinRes = await client.JoinGameAsync(roomId);
    if (!joinRes.Success) { PrintError(joinRes.Error!); return; }

    // 3. Waiting Room / Lobby Loop
    bool gameStarted = false;
    
    // Subscribe to join/leave events to refresh lobby UI
    client.OnPlayerJoined += (_, _, _) => { if (!gameStarted) RenderLobby(client, roomId); };
    client.OnPlayerLeft += (_, _) => { if (!gameStarted) RenderLobby(client, roomId); };
    client.OnStateUpdated += (_) => { if (!gameStarted) RenderLobby(client, roomId); };

    while (!gameStarted)
    {
        RenderLobby(client, roomId);
        
        // Check start conditions
        var playerCount = client.State?.ActiveSeatsMask != null 
            ? System.Numerics.BitOperations.PopCount(client.State.ActiveSeatsMask) 
            : 1;

        if (playerCount > 1)
        {
            Console.WriteLine("\n[!] Opponents found! Starting game in 3 seconds...");
            await Task.Delay(3000);
            gameStarted = true;
        }
        else
        {
            Console.WriteLine("\nWaiting for opponents... (Press 'Q' to leave)");
            if (Console.KeyAvailable)
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Q) return;
            }
            await Task.Delay(1000);
        }
    }

    // 4. Gameplay Loop
    client.OnStateUpdated += _ => RenderLudoBoard(client);
    client.OnDiceRolled += (seat, val) => 
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n🎲 Player {seat} rolled a {val}!");
        Console.ResetColor();
        Thread.Sleep(1500);
    };

    // Initial Render
    RenderLudoBoard(client);

    bool inGame = true;
    while (inGame)
    {
        if (client.IsGameOver)
        {
            Console.WriteLine("\n🏁 GAME OVER! 🏁");
            Console.ReadKey();
            break;
        }

        if (client.IsMyTurn)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n👉 YOUR TURN!");
            Console.ResetColor();

            if (client.LastDiceRoll == 0)
            {
                Console.WriteLine("Press [ENTER] to Roll Dice");
                Console.ReadLine();
                var roll = await client.RollDiceAsync();
                if (!roll.Success) Console.WriteLine($"! {roll.Error}");
            }
            else
            {
                var legalMoves = client.GetMovableTokens();
                if (legalMoves.Length == 0)
                {
                    Console.WriteLine("No legal moves. Passing turn...");
                    await Task.Delay(2000);
                }
                else if (legalMoves.Length == 1)
                {
                    Console.WriteLine("Auto-moving only legal token...");
                    await client.MoveTokenAsync(legalMoves[0]);
                }
                else
                {
                    Console.WriteLine($"Choose Token: [{string.Join(", ", legalMoves)}]");
                    Console.Write("> ");
                    var input = Console.ReadLine();
                    if (int.TryParse(input, out int tIdx) && legalMoves.Contains(tIdx))
                    {
                        await client.MoveTokenAsync(tIdx);
                    }
                }
            }
        }
        else
        {
            await Task.Delay(500);
        }
    }
    
    await socket.LeaveRoomAsync();
}

// ==========================================
// 4. LUCKY MINE FLOW
// ==========================================
async Task RunLuckyMineFlow(GameClient socket)
{
    var client = new LuckyMineClient(socket);
    WriteHeader("LUCKY MINE");
    
    // We use templates here because it's single player, created on demand
    Console.WriteLine("1. Normal (5 Mines)");
    Console.WriteLine("2. High Risk (15 Mines)");
    Console.Write("> ");
    var tName = Console.ReadLine() == "2" ? "High Risk Mines" : "5Mines";

    var res = await client.StartGameAsync(tName);
    if (!res.Success) { PrintError(res.Error!); return; }

    while (client.IsActive)
    {
        RenderMineField(client);
        Console.WriteLine("\nCommands: [0-24] Reveal Tile | [C] Cashout");
        Console.Write("> ");
        var cmd = Console.ReadLine()?.ToUpper();

        if (cmd == "C") await client.CashOutAsync();
        else if (int.TryParse(cmd, out int tIdx)) await client.RevealTileAsync(tIdx);
    }

    RenderMineField(client);
    if (client.HitMine) Console.WriteLine("\n💥 BOOM! Game Over.");
    if (client.CashedOut) Console.WriteLine($"\n💰 WON {client.CurrentWinnings:N0} COINS!");
    
    Console.WriteLine("Press any key...");
    Console.ReadKey();
    await socket.LeaveRoomAsync();
}

// ==========================================
// 5. UI HELPERS
// ==========================================
void RenderLobby(LudoClient client, string roomId)
{
    Console.Clear();
    WriteHeader($"LOBBY: {roomId}");
    
    Console.WriteLine("Waiting for players to fill the room...\n");
    
    for (int i = 0; i < 4; i++)
    {
        var isActive = client.IsSeatActive(i);
        var isMe = client.MySeat == i;
        
        Console.Write($"Seat {i}: ");
        if (isActive)
        {
            Console.ForegroundColor = isMe ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(isMe ? "YOU" : "OCCUPIED");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Waiting...");
        }
        Console.ResetColor();
    }
}

void RenderLudoBoard(LudoClient client)
{
    Console.Clear();
    WriteHeader($"LUDO ROOM");

    // Top Status Bar
    Console.BackgroundColor = ConsoleColor.DarkGray;
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($" P{client.State?.CurrentPlayer}'s TURN | Roll: {(client.State?.LastDiceRoll == 0 ? "-" : client.State?.LastDiceRoll.ToString())} ".PadRight(42));
    Console.ResetColor();
    Console.WriteLine();

    // Player Lanes
    for (int p = 0; p < 4; p++)
    {
        if (!client.IsSeatActive(p)) continue;
        
        var isTurn = client.State?.CurrentPlayer == p;
        var color = p switch { 0 => ConsoleColor.Red, 1 => ConsoleColor.Green, 2 => ConsoleColor.Yellow, 3 => ConsoleColor.Blue, _ => ConsoleColor.White };
        
        if (isTurn) Console.Write("👉 "); else Console.Write("   ");
        
        Console.ForegroundColor = color;
        Console.Write($"PLAYER {p}");
        if (p == client.MySeat) Console.Write(" (YOU)");
        Console.ResetColor();
        Console.WriteLine();

        var tokens = client.GetPlayerTokens(p);
        Console.Write("   ");
        for (int t = 0; t < 4; t++)
        {
            var pos = tokens[t];
            var display = pos switch { 0 => " B ", 57 => "WIN", _ => pos.ToString("000") };
            
            // Highlight movable tokens
            if (isTurn && client.MySeat == p && client.GetMovableTokens().Contains(t))
            {
                Console.BackgroundColor = ConsoleColor.White;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            Console.Write($"[{display}]");
            Console.ResetColor();
            Console.Write(" ");
        }
        Console.WriteLine("\n");
    }
}

void RenderMineField(LuckyMineClient client)
{
    Console.Clear();
    WriteHeader("LUCKY MINE");
    Console.WriteLine($"Winnings: {client.CurrentWinnings:N0} | Mines: {client.TotalMines}");
    Console.WriteLine("-------------------------");
    
    for (int i = 0; i < client.TotalTiles; i++)
    {
        if (client.IsTileRevealed(i))
        {
            if (client.IsTileMine(i))
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.Write("[ X ]");
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.Green;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[ $ ]");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{i:00}]");
        }
        Console.ResetColor();
        
        if ((i + 1) % 5 == 0) Console.WriteLine(); else Console.Write(" ");
    }
}

void WriteHeader(string title)
{
    Console.Clear();
    Console.WriteLine("==========================================");
    Console.WriteLine($"   {title}");
    Console.WriteLine("==========================================");
}

void PrintSuccess() { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("OK"); Console.ResetColor(); }
void PrintError(string err) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"FAILED: {err}"); Console.ResetColor(); }