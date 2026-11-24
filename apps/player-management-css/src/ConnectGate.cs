using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace PlayerManagement;

// A restricted server's door: the ConnectClient hook the match and practice
// plugins use, deciding against the panel's access list. A community server
// never loads either of those, so this is the only hook on it.
public partial class PlayerManagementPlugin
{
    private static readonly nint RefusedPasswordBuffer = Marshal.StringToCoTaskMemUTF8(
        ConnectRefusal.Password
    );

    // FiveStack_ConnectClient in shared/gamedata/fivestack.gamedata.json, which
    // the gamedata validator checks after every game update.
    // near "CNetworkGameServerBase::ConnectClient( name=\'%s\', remote=\'%s\' )\n"
    private static readonly string ConnectClientSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? "55 48 89 E5 41 57 49 89 D7 41 56 49 89 FE 41 55 41 54 53 89 CB 48 81 EC ? ? ? ?"
        : "48 89 5C 24 18 44 89 4C 24 20 55 41 54 41 55 41 56 41 57 48 8D 6C 24 F1 48 81 EC ? ? ? ? 81 64 24 4C FF FF 0F FF";

    /// <summary>
    /// <c>
    /// virtual CServerSideClientBase* CNetworkGameServerBase::ConnectClient(
    /// 	const char* name,
    /// 	ns_address* address,
    /// 	void* netInfo,
    /// 	C2S_CONNECT_Message* connectMsg,
    /// 	const char* password,
    /// 	const byte* authTicket,
    /// 	int authTicketLength,
    /// 	bool isLowViolence);
    /// </c>
    /// Built on load rather than as a static like the match and practice
    /// plugins: a signature the game update broke would throw from the type
    /// initializer and take bans, mutes and gags down with the door.
    /// </summary>
    private MemoryFunctionWithReturn<
        nint,
        nint,
        nint,
        nint,
        nint,
        nint,
        nint,
        int,
        bool,
        nint
    >? _connectClientFunc;

    private nint _passwordBuffer = nint.Zero;
    private string? _bufferedPassword;

    private void InstallConnectGate()
    {
        try
        {
            _connectClientFunc = new(ConnectClientSignature, Addresses.EnginePath);
            _connectClientFunc.Hook(OnConnectClient, HookMode.Pre);

            Logger.LogInformation("ConnectClient hook installed for access lists");
        }
        catch (Exception error)
        {
            _connectClientFunc = null;
            Logger.LogError(
                error,
                "unable to hook ConnectClient; access lists are only enforced by kicking after join"
            );
        }
    }

    private void UninstallConnectGate()
    {
        try
        {
            _connectClientFunc?.Unhook(OnConnectClient, HookMode.Pre);
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to remove the ConnectClient hook");
        }

        _connectClientFunc = null;

        if (_passwordBuffer != nint.Zero)
        {
            Marshal.FreeCoTaskMem(_passwordBuffer);
            _passwordBuffer = nint.Zero;
            _bufferedPassword = null;
        }
    }

    // Anything thrown here would unwind through the engine, so a failure
    // leaves the connect exactly as the client sent it.
    private HookResult OnConnectClient(DynamicHook hook)
    {
        try
        {
            ulong steamId = ServerAccessBook.TicketSteamId(
                hook.GetParam<nint>(6),
                hook.GetParam<int>(7)
            );

            // A player this server kicked for a ban, still banned: refused
            // here on a password server. Without one, Enforce kicks them again
            // (an empty ticket loops).
            if (
                _rejoin.Refuse(steamId, _book, DateTimeOffset.UtcNow)
                && ConnectRefusal.ByPassword(ConVar.Find("sv_password")?.StringValue)
            )
            {
                _loop?.Request();
                Logger.LogInformation("refusing banned player {steamId} at connect", steamId);
                hook.SetParam(5, RefusedPasswordBuffer);

                return HookResult.Continue;
            }

            eServerAccess access = _access.Decide(steamId.ToString());

            if (steamId != 0)
            {
                // Its own guard: a failure here must not skip the password swap.
                try
                {
                    _access.Connecting(
                        steamId.ToString(),
                        access == eServerAccess.Denied,
                        Humans().Any(player =>
                            SteamIdOf(player) == steamId
                            && player.Connected == PlayerConnectedState.Connected
                        )
                    );
                }
                catch (Exception error)
                {
                    Logger.LogError(error, "access bookkeeping failed for {steamId}", steamId);
                }
            }

            if (access is eServerAccess.Unknown or eServerAccess.Open)
            {
                return HookResult.Continue;
            }

            nint password = access == eServerAccess.Allowed ? ServerPassword() : nint.Zero;

            Logger.LogInformation(
                "connect {steamId} '{name}': {access} by access list {version} | password swapped: {swapped}",
                steamId,
                hook.GetParam<string>(1) ?? "",
                access,
                _access.Snapshot().Version,
                password != nint.Zero
            );

            // Without a password there is no clean refusal at the door (an
            // empty ticket loops), so a refused player is let in and kicked
            // with the reason by Enforce.
            if (
                access == eServerAccess.Denied
                && ConnectRefusal.ByPassword(ConVar.Find("sv_password")?.StringValue)
            )
            {
                hook.SetParam(5, RefusedPasswordBuffer);
            }
            else if (password != nint.Zero)
            {
                hook.SetParam(5, password);
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "access check failed; leaving the connect to the engine");
        }

        return HookResult.Continue;
    }

    // Read at connect time: the owner can change sv_password over RCON while
    // the server hibernates, when nothing on a tick would notice.
    private nint ServerPassword()
    {
        string password = ConVar.Find("sv_password")?.StringValue ?? "";

        if (password.Length == 0)
        {
            return nint.Zero;
        }

        if (password != _bufferedPassword)
        {
            if (_passwordBuffer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(_passwordBuffer);
            }

            _passwordBuffer = Marshal.StringToCoTaskMemUTF8(password);
            _bufferedPassword = password;
        }

        return _passwordBuffer;
    }
}
