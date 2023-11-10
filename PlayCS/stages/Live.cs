using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void startLive()
    {
        if (Phase != ePhase.Warmup && Phase != ePhase.Knife)
        {
            return;
        }

        Console.WriteLine("START LIVE");
    }
}
