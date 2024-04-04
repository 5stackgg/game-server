using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FiveStack;

public class FiveStackServiceCollection : IPluginServiceCollection<FiveStackPlugin>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<GameEvents>();
        serviceCollection.AddScoped<GameServer>();
        serviceCollection.AddScoped<MatchService>();
        serviceCollection.AddScoped<MatchDemos>();
        serviceCollection.AddScoped<BackUpManagement>();
        serviceCollection.AddScoped<EnvironmentService>();

        serviceCollection.AddTransient<MatchReadySystem>();
        serviceCollection.AddTransient<MatchCoachSystem>();
        serviceCollection.AddTransient<MatchCaptainSystem>();
        serviceCollection.AddTransient<MatchTimeoutSystem>();
    }
}
