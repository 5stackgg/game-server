using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;

namespace NadePractice;

// CounterStrikeSharp discovers this automatically and builds the plugin's
// container from it, mirroring the match plugin's FiveStackServiceCollection.
public class NadePracticeServiceCollection : IPluginServiceCollection<NadePracticePlugin>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<NadesConfig>();
        serviceCollection.AddSingleton<NadesApiClient>();
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
