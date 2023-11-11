using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerConnected()
    {
        RegisterEventHandler<EventPlayerConnect>(
            (@event, info) =>
            {
                if (@event.Userid == null || _matchData == null)
                {
                    return HookResult.Continue;
                }

                CCSPlayerController? player = @event.Userid;

                _redis.PublishMatchEvent(
                    _matchData.id,
                    new Redis.EventData<Dictionary<string, object>>
                    {
                        @event = "player",
                        data = new Dictionary<string, object>
                        {
                            { "steam_id", player.SteamID },
                            { "player_name", player.PlayerName },
                        }
                    }
                );

                MatchMember? foundMatchingMember = _matchData
                    .members
                    .Find(member =>
                    {
                        if (member.steam_id == null)
                        {
                            return member.name.StartsWith(player.PlayerName);
                        }
                        return (ulong)member.steam_id == player.SteamID;
                    });

                if (foundMatchingMember != null)
                {
                    MatchTeam? team = _matchData
                        .teams
                        .Find(team =>
                        {
                            return team.id == foundMatchingMember.team_id;
                        });

                    if (team != null)
                    {
                        CsTeam startingSide = TeamStringToCsTeam(team.starting_side);
                        if (TeamNumToCSTeam(player.TeamNum) != startingSide)
                        {
                            Console.WriteLine(
                                $"Switching {player.PlayerName} to {team.starting_side}"
                            );
                            player.SwitchTeam(startingSide);
                        }
                    }
                }

                return HookResult.Continue;
            }
        );
    }
}

// TODO - error
//   used during construction differ from defaults. Please re-export the map.
// Nav mesh (v33/1) loaded with 0 nav areas for 3 hulls.
// CNavGenParams - Nav mesh requests project default generation parameters but actual parameters
//   used during construction differ from defaults. Please re-exportSystem.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
//  ---> System.NullReferenceException: Object reference not set to an instance of an object.
//    at PlayCs.PlayCsPlugin.<CapturePlayerConnected>b__27_0(EventPlayerConnect event, GameEventInfo info) in /opt/playcs/PlayCS/events/PlayerConnected.cs:line 21
//    at InvokeStub_GameEventHandler`1.Invoke(Object, Object, IntPtr*)
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    --- End of inner exception stack trace ---
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
//    at System.Delegate.DynamicInvokeImpl(Object[] args)
//    at CounterStrikeSharp.API.Core.FunctionReference.<>c__DisplayClass3_0.<.ctor>b__0(fxScriptContext* context) in /__w/CounterStrikeSharp/CounterStrikeSharp/managed/CounterStrikeSharp.API/Core/FunctionReference.cs:line 70
// System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
//  ---> System.NullReferenceException: Object reference not set to an instance of an object.
//    at PlayCs.PlayCsPlugin.<CapturePlayerConnected>b__27_0(EventPlayerConnect event, GameEventInfo info) in /opt/playcs/PlayCS/events/PlayerConnected.cs:line 21
//    at InvokeStub_GameEventHandler`1.Invoke(Object, Object, IntPtr*)
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    --- End of inner exception stack trace ---
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
//    at System.Delegate.DynamicInvokeImpl(Object[] args)
//    at CounterStrikeSharp.API.Core.FunctionReference.<>c__DisplayClass3_0.<.ctor>b__0(fxScriptContext* context) in /__w/CounterStrikeSharp/CounterStrikeSharp/managed/CounterStrikeSharp.API/Core/FunctionReference.cs:line 70
// System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
//  ---> System.NullReferenceException: Object reference not set to an instance of an object.
//    at PlayCs.PlayCsPlugin.<CapturePlayerConnected>b__27_0(EventPlayerConnect event, GameEventInfo info) in /opt/playcs/PlayCS/events/PlayerConnected.cs:line 21
//    at InvokeStub_GameEventHandler`1.Invoke(Object, Object, IntPtr*)
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    --- End of inner exception stack trace ---
//    at System.Reflection.MethodInvoker.Invoke(Object obj, IntPtr* args, BindingFlags invokeAttr)
//    at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
//    at System.Delegate.DynamicInvokeImpl(Object[] args)
//    at CounterStrikeSharp.API.Core.FunctionReference.<>c__DisplayClass3_0.<.ctor>b__0(fxScriptContext* context) in /__w/CounterStrikeSharp/CounterStrikeSharp/managed/CounterStrikeSharp.API/Core/FunctionReference.cs:line 70
//  the map.
