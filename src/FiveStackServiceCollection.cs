using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FiveStack;

public class FiveStackServiceCollection : IPluginServiceCollection<FiveStackPlugin>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<MatchService>();

        serviceCollection.AddScoped<MatchEvents>();
        serviceCollection.AddScoped<GameServer>();
        serviceCollection.AddScoped<GameDemos>();
        serviceCollection.AddScoped<GameBackUpRounds>();
        serviceCollection.AddScoped<EnvironmentService>();

        serviceCollection.AddTransient<MatchManager>();
        serviceCollection.AddTransient<KnifeSystem>();
        serviceCollection.AddTransient<ReadySystem>();
        serviceCollection.AddTransient<CoachSystem>();
        serviceCollection.AddTransient<CaptainSystem>();
        serviceCollection.AddTransient<Timeouts>();
    }
}
