using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin : BasePlugin
{
    public CsTeam? KnifeWinningTeam;
    public eMapStatus _currentMapStatus = eMapStatus.Unknown;

    public override void Load(bool hotReload)
    {
        ListenForMapChange();
        ListenForReadyStatus();

        Message(HudDestination.Alert, "5Stack Loaded");
    }
}
