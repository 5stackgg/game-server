using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;

namespace UtilityPractice;

// CounterStrikeSharp discovers this automatically and builds the plugin's
// container from it, mirroring the match plugin's FiveStackServiceCollection.
public class UtilityPracticeServiceCollection : IPluginServiceCollection<UtilityPracticePlugin>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<UtilityConfig>();
        serviceCollection.AddSingleton<UtilityApiClient>();
        serviceCollection.AddSingleton<PracticeSession>();
        serviceCollection.AddSingleton<PracticeRecorder>();
        serviceCollection.AddSingleton<PracticeLibrary>();
        serviceCollection.AddSingleton<PracticeReplay>();
        serviceCollection.AddSingleton<PracticeSystem>();
        serviceCollection.AddSingleton<PracticeScore>();
        serviceCollection.AddSingleton<PracticePlaybook>();
        serviceCollection.AddSingleton<PracticeDrill>();
    }
}
