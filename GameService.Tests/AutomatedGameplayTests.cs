using System.Net.Http.Json;
using System.Text.Json;
using GameService.ApiService.Hubs;
using GameService.GameCore;
using Microsoft.AspNetCore.SignalR.Client;
using Projects;

namespace GameService.Tests;

[TestFixture]
public class AutomatedGameplayTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task FullGameLoop_Ludo_RollsDice_Successfully()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<GameService_AppHost>(cancellationToken);

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
            clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(TestTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(TestTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken)
            .WaitAsync(TestTimeout, cancellationToken);

        var httpClient = app.CreateHttpClient("apiservice");
        var email = $"autobot_{Guid.NewGuid():N}@gameservice.local";
        var password = "TestPass123!";

        var registerResp =
            await httpClient.PostAsJsonAsync("/auth/register", new { email, password }, cancellationToken);
        registerResp.EnsureSuccessStatusCode();

        var loginResp = await httpClient.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken);
        loginResp.EnsureSuccessStatusCode();

        using var loginDoc = await JsonDocument.ParseAsync(await loginResp.Content.ReadAsStreamAsync(cancellationToken),
            default, cancellationToken);
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.That(accessToken, Is.Not.Null, "Access Token should not be null");

        var hubUrl = new Uri(httpClient.BaseAddress!, "/hubs/game").ToString();
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                options.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler clientHandler)
                        clientHandler.ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    return handler;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        var diceRolledTcs = new TaskCompletionSource<int>();
        var gameStateTcs = new TaskCompletionSource<bool>();

        connection.On<object>("DiceRolled", data =>
        {
            var json = JsonSerializer.Serialize(data);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var val)) diceRolledTcs.TrySetResult(val.GetInt32());
        });

        connection.On<object>("GameState", data => { gameStateTcs.TrySetResult(true); });

        await connection.StartAsync(cancellationToken);

        var createResponse =
            await connection.InvokeAsync<CreateRoomResponse>("CreateRoom", "Classic Ludo (4P)", cancellationToken);
        Assert.That(createResponse.Success, Is.True, "Room creation failed");

        var roomId = createResponse.RoomId!;
        Console.WriteLine($"Test Room Created: {roomId}");

        var joinResponse = await connection.InvokeAsync<JoinRoomResponse>("JoinRoom", roomId, cancellationToken);
        Assert.That(joinResponse.Success, Is.True, "Joining room failed");

        var actionPayload = JsonSerializer.SerializeToElement(new { });

        var actionResult = await connection.InvokeAsync<GameActionResult>(
            "PerformAction",
            roomId,
            "roll",
            actionPayload,
            cancellationToken);

        Assert.That(actionResult.Success, Is.True, $"Roll action failed: {actionResult.ErrorMessage}");

        var waitTask = Task.WhenAny(diceRolledTcs.Task, Task.Delay(5000, cancellationToken));
        Assert.That(await waitTask, Is.EqualTo(diceRolledTcs.Task), "Timed out waiting for DiceRolled event");

        var diceValue = await diceRolledTcs.Task;
        Assert.That(diceValue, Is.InRange(1, 6), "Dice value should be between 1 and 6");

        Console.WriteLine($"Verified: Dice rolled a {diceValue}");

        await connection.StopAsync(cancellationToken);
        await app.StopAsync(cancellationToken);
    }
}