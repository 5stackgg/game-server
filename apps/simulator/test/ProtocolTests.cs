using System.Collections;
using System.Net.Sockets;
using System.Text.Json;
using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

public class ProtocolTests
{
    [Fact]
    public void EventsGoOutInThePluginsEnvelope()
    {
        Guid matchId = Guid.NewGuid();
        Guid mapId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        string json = EventMessages.Envelope(
            matchId,
            mapId,
            messageId,
            "kill",
            new Dictionary<string, object> { { "round", 4 }, { "headshot", true } }
        );

        JsonElement root = JsonDocument.Parse(json).RootElement;
        JsonElement data = root.GetProperty("data");

        Assert.Equal("events", root.GetProperty("event").GetString());
        Assert.Equal(matchId, data.GetProperty("matchId").GetGuid());
        Assert.Equal(mapId, data.GetProperty("mapId").GetGuid());
        Assert.Equal(messageId, data.GetProperty("messageId").GetGuid());
        Assert.Equal("kill", data.GetProperty("data").GetProperty("event").GetString());
        Assert.Equal(
            4,
            data.GetProperty("data").GetProperty("data").GetProperty("round").GetInt32()
        );
        Assert.True(
            data.GetProperty("data").GetProperty("data").GetProperty("headshot").GetBoolean()
        );
    }

    [Fact]
    public void TheApiAcknowledgesWithTheMessageIdAsAJsonString()
    {
        Guid messageId = Guid.NewGuid();

        Assert.Equal(messageId, EventMessages.Ack($"\"{messageId}\""));
        Assert.Null(EventMessages.Ack("not an id"));
    }

    [Fact]
    public void ThePingSaysWhichMapIsLoadedAndNeverClaimsTheRelay()
    {
        string path = ApiClient.PingPath("server-1", "de_dust2", "dev", 10);

        Assert.StartsWith("/game-server-node/ping/server-1?", path);
        Assert.Contains("map=de_dust2", path);
        Assert.Contains("mapName=de_dust2", path);
        Assert.Contains("players=10", path);
        Assert.Contains("hibernating=false", path);
        Assert.DoesNotContain("steamRelay", path);
    }

    [Fact]
    public void AnEmptyCurrentMatchClearsTheServer()
    {
        Assert.Equal(eCurrentMatchAction.Clear, ApiClient.Read("").Action);
        Assert.Equal(eCurrentMatchAction.Ignore, ApiClient.Read("{not json").Action);

        MatchFetch fetch = ApiClient.Read(
            JsonSerializer.Serialize(new { id = Guid.NewGuid(), status = "WaitingForServer" })
        );

        Assert.Equal(eCurrentMatchAction.Setup, fetch.Action);
        Assert.Equal("WaitingForServer", fetch.MatchStatus);
    }

    [Fact]
    public void SettingsComeFromTheJobsEnvironment()
    {
        SimulatorSettings settings = SimulatorSettings.FromEnvironment(
            new Hashtable
            {
                { "SERVER_ID", "server-1" },
                { "SERVER_API_PASSWORD", "secret" },
                { "API_DOMAIN", "https://api.5stack.localhost" },
                { "WS_DOMAIN", "wss://ws.5stack.localhost" },
                { "SERVER_PORT", "30015" },
                { "RCON_PASSWORD", "rcon" },
                { "GAME_NODE_SERVER", "true" },
                {
                    "EXTRA_GAME_PARAMS",
                    "-disable_workshop_command_filtering -maxplayers 13 +map de_ancient +game_type 0"
                },
                { "SIM_SPEED", "60" },
                { "SIM_SEED", "7" },
            }
        );

        Assert.Equal("server-1", settings.ServerId);
        Assert.Equal(30015, settings.Port);
        Assert.Equal("de_ancient", settings.StartMap);
        Assert.True(settings.OnGameNode);
        Assert.Equal(60, settings.Speed);
        Assert.Equal(7, settings.Seed);
        Assert.Equal("dev", settings.PluginVersion);
    }

    [Fact]
    public void SpeedFallsBackToItsDefault()
    {
        SimulatorSettings settings = SimulatorSettings.FromEnvironment(
            new Hashtable { { "SIM_SPEED", "fast" } }
        );

        Assert.Equal(SimulatorSettings.DefaultSpeed, settings.Speed);
        Assert.Equal("de_inferno", settings.StartMap);
    }

    private static async Task<(int Id, int Type, string Body)> Exchange(
        NetworkStream stream,
        int id,
        int type,
        string body
    )
    {
        await stream.WriteAsync(RconPacket.Encode(id, type, body));
        return (await RconPacket.Read(stream, CancellationToken.None))!.Value;
    }

    [Fact]
    public async Task RconAuthenticatesAndRunsCommandsLikeSourceDoes()
    {
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource();
        var commands = new List<string>();
        var rcon = new RconServer(
            0,
            "rcon",
            command =>
            {
                commands.Add(command);
                return $"ran {command}";
            }
        );
        _ = rcon.Run(stop.Token, listening);
        await listening.Task;

        using (var refused = new TcpClient())
        {
            await refused.ConnectAsync("127.0.0.1", rcon.Port);
            (int id, int type, _) = await Exchange(
                refused.GetStream(),
                0,
                RconPacket.Auth,
                "wrong"
            );

            Assert.Equal(-1, id);
            Assert.Equal(RconPacket.AuthResponse, type);
        }

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", rcon.Port);
        NetworkStream stream = client.GetStream();

        // Source answers an auth with an empty response first, which is the
        // packet rcon-client takes as the answer
        (int authId, int authType, _) = await Exchange(stream, 0, RconPacket.Auth, "rcon");
        Assert.Equal(0, authId);
        Assert.Equal(RconPacket.ResponseValue, authType);
        (int confirmedId, int confirmedType, _) = (
            await RconPacket.Read(stream, CancellationToken.None)
        )!.Value;
        Assert.Equal(0, confirmedId);
        Assert.Equal(RconPacket.AuthResponse, confirmedType);

        (int replyId, int replyType, string reply) = await Exchange(
            stream,
            5,
            RconPacket.ExecCommand,
            "get_match"
        );
        Assert.Equal(5, replyId);
        Assert.Equal(RconPacket.ResponseValue, replyType);
        Assert.Equal("ran get_match", reply);
        Assert.Equal(["get_match"], commands);

        stop.Cancel();
    }

    [Fact]
    public void ConsoleCommandsAreSplitLikeTheServerConsoleDoes()
    {
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(new FakeSource(), sink);

        string reply = server.Execute("fivestack_status; restore_round 3");

        Assert.Contains("Plugin Runtime: simulator", reply);
        Assert.Contains($"{SimulatedServer.RestoreRefused} ", reply);
    }
}
