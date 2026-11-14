using System.Text.Json;
using FiveStack.Utilities;
using Xunit;

public class RoundMoneyTests
{
    private const ulong Player = 76561198000000001;

    private const ulong Other = 76561198000000002;

    private static Dictionary<ulong, int> StartedWith(params PlayerMoney[] players)
    {
        var atRoundStart = new Dictionary<ulong, int>();
        RoundMoney.Baseline(atRoundStart, players);
        return atRoundStart;
    }

    [Fact]
    public void EarnedIsWhatTheMapTotalGrewByThisRound()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(new PlayerMoney(Player, 0, 9400));

        Assert.Equal(3550, RoundMoney.Earned(atRoundStart, new PlayerMoney(Player, 4700, 12950)));
    }

    [Fact]
    public void APlayerWhoJoinedMidRoundEarnedNothing()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(new PlayerMoney(Other, 0, 9400));

        Assert.Equal(0, RoundMoney.Earned(atRoundStart, new PlayerMoney(Player, 0, 12950)));
    }

    [Fact]
    public void EarnedIsNeverNegative()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(new PlayerMoney(Player, 0, 9400));

        Assert.Equal(0, RoundMoney.Earned(atRoundStart, new PlayerMoney(Player, 0, 800)));
    }

    [Fact]
    public void AnUnreadablePlayerEarnedNothing()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(new PlayerMoney(Player, 0, null));

        Assert.Empty(atRoundStart);
        Assert.Equal(0, RoundMoney.Earned(atRoundStart, new PlayerMoney(Player, 0, 12950)));
        Assert.Equal(
            0,
            RoundMoney.Earned(
                StartedWith(new PlayerMoney(Player, 0, 9400)),
                new PlayerMoney(Player, 0, null)
            )
        );
    }

    [Fact]
    public void EachRoundStartForgetsTheLast()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(
            new PlayerMoney(Player, 0, 9400),
            new PlayerMoney(Other, 0, 7000)
        );

        RoundMoney.Baseline(atRoundStart, [new PlayerMoney(Player, 0, 12950)]);

        Assert.Equal(Player, Assert.Single(atRoundStart.Keys));
        Assert.Equal(12950, atRoundStart[Player]);
    }

    [Fact]
    public void ThePayloadNamesEachRealPlayerBySteamId()
    {
        Dictionary<ulong, int> atRoundStart = StartedWith(
            new PlayerMoney(Player, 0, 9400),
            new PlayerMoney(Other, 0, 7000)
        );

        JsonElement sent = JsonDocument
            .Parse(
                JsonSerializer.Serialize(
                    RoundMoney.Payload(
                        atRoundStart,
                        [
                            new PlayerMoney(Player, 4700, 12950),
                            new PlayerMoney(Other, 200, 8400),
                            new PlayerMoney(0, 1000, 5000),
                        ]
                    )
                )
            )
            .RootElement;

        Assert.Equal(2, sent.GetArrayLength());

        JsonElement first = sent[0];
        Assert.Equal(Player.ToString(), first.GetProperty("steam_id").GetString());
        Assert.Equal(4700, first.GetProperty("spent").GetInt32());
        Assert.Equal(3550, first.GetProperty("earned").GetInt32());

        JsonElement second = sent[1];
        Assert.Equal(Other.ToString(), second.GetProperty("steam_id").GetString());
        Assert.Equal(200, second.GetProperty("spent").GetInt32());
        Assert.Equal(1400, second.GetProperty("earned").GetInt32());
    }
}
