using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FiveStack;

public class FiveStackServiceCollection : IPluginServiceCollection<FiveStackPlugin>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<MatchService>();

        serviceCollection.AddSingleton<MatchEvents>();
        serviceCollection.AddSingleton<GameServer>();
        serviceCollection.AddSingleton<GameDemos>();
        serviceCollection.AddSingleton<GameBackUpRounds>();
        serviceCollection.AddSingleton<SurrenderSystem>();
        serviceCollection.AddSingleton<EnvironmentService>();
        serviceCollection.AddSingleton<SteamAPI>();
        serviceCollection.AddSingleton<INetworkServerService>();

        serviceCollection.AddSingleton<CoachSystem>();
        serviceCollection.AddSingleton<CaptainSystem>();

        serviceCollection.AddTransient<MatchManager>();
        serviceCollection.AddTransient<VoteSystem>();
        serviceCollection.AddTransient<KnifeSystem>();
        serviceCollection.AddTransient<ReadySystem>();
        serviceCollection.AddTransient<TimeoutSystem>();

        serviceCollection.AddSingleton<IMatchUtilityService, MatchUtilityService>();
        serviceCollection.AddSingleton<ICommandService, CommandService>();
        serviceCollection.AddSingleton<IConVarService, ConVarService>();
        serviceCollection.AddSingleton<IGameStateService, GameStateService>();
        serviceCollection.AddSingleton<IPlayerService, PlayerService>();
        serviceCollection.AddSingleton<ITimerService, TimerService>();
    }
}
