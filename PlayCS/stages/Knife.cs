using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void startKnife()
    {
        if (Phase != ePhase.Warmup)
        {
            return;
        }

        Console.WriteLine("START KNIFE");
    }
}
