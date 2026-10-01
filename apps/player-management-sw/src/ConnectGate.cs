using System.Runtime.InteropServices;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;

namespace PlayerManagement;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate nint ConnectClientDelegate(
    nint server,
    nint name,
    nint address,
    nint netInfo,
    nint connectMsg,
    nint password,
    nint authTicket,
    int authTicketLength,
    bool isLowViolence
);

// A restricted server's door: the ConnectClient hook the match and practice
// plugins use, deciding against the panel's access list. A community server
// never loads either of those, so this is the only hook on it.
public partial class PlayerManagementPlugin
{
    /**
     * FiveStack_ConnectClient in shared/gamedata/fivestack.gamedata.json, which
     * the gamedata validator checks after every game update. Signature near:
     *     "CNetworkGameServerBase::ConnectClient( name='%s', remote='%s' )\n"
     *
     * Function signature:
     * <pre>
     * virtual CServerSideClientBase* CNetworkGameServerBase::ConnectClient(
     *     const char* name,
     *     ns_address* address,
     *     void* netInfo,
     *     C2S_CONNECT_Message* connectMsg,
     *     const char* password,
     *     const byte* authTicket,
     *     int authTicketLength,
     *     bool isLowViolence
     * );
     * </pre>
     */
    private static readonly string ConnectClientSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? "55 48 89 E5 41 57 49 89 D7 41 56 49 89 FE 41 55 41 54 53 89 CB 48 81 EC ? ? ? ?"
        : "48 89 5C 24 18 44 89 4C 24 20 55 41 54 41 55 41 56 41 57 48 8D 6C 24 F1 48 81 EC ? ? ? ? 81 64 24 4C FF FF 0F FF";

    private IUnmanagedFunction<ConnectClientDelegate>? _connectClientFunc;
    private Guid _connectClientHookId;

    private nint _passwordBuffer = nint.Zero;
    private string? _bufferedPassword;

    private void InstallConnectGate()
    {
        try
        {
            nint? found = Core.Memory.GetAddressBySignature(Library.Engine, ConnectClientSignature);

            if (found == null || found == nint.Zero)
            {
                _logger.LogError(
                    "ConnectClient signature not found; access lists are only enforced by kicking after join"
                );
                return;
            }

            _connectClientFunc = Core.Memory.GetUnmanagedFunctionByAddress<ConnectClientDelegate>(
                found.Value
            );

            _connectClientHookId = _connectClientFunc.AddHook(next =>
                (
                    server,
                    name,
                    address,
                    netInfo,
                    connectMsg,
                    password,
                    authTicket,
                    authTicketLength,
                    isLowViolence
                ) =>
                {
                    (eServerAccess access, nint swapped) = OnConnectClient(
                        name,
                        authTicket,
                        authTicketLength
                    );

                    if (access == eServerAccess.Denied)
                    {
                        return next()(
                            server,
                            name,
                            address,
                            netInfo,
                            connectMsg,
                            password,
                            nint.Zero,
                            0,
                            isLowViolence
                        );
                    }

                    return next()(
                        server,
                        name,
                        address,
                        netInfo,
                        connectMsg,
                        swapped != nint.Zero ? swapped : password,
                        authTicket,
                        authTicketLength,
                        isLowViolence
                    );
                }
            );

            _logger.LogInformation("ConnectClient hook installed for access lists");
        }
        catch (Exception error)
        {
            _connectClientFunc = null;
            _logger.LogError(
                error,
                "unable to hook ConnectClient; access lists are only enforced by kicking after join"
            );
        }
    }

    private void UninstallConnectGate()
    {
        try
        {
            if (_connectClientFunc != null && _connectClientHookId != Guid.Empty)
            {
                _connectClientFunc.RemoveHook(_connectClientHookId);
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to remove the ConnectClient hook");
        }

        _connectClientFunc = null;
        _connectClientHookId = Guid.Empty;

        if (_passwordBuffer != nint.Zero)
        {
            Marshal.FreeCoTaskMem(_passwordBuffer);
            _passwordBuffer = nint.Zero;
            _bufferedPassword = null;
        }
    }

    // Anything thrown here would unwind through the engine, so a failure
    // leaves the connect exactly as the client sent it.
    private (eServerAccess Access, nint Password) OnConnectClient(
        nint name,
        nint authTicket,
        int authTicketLength
    )
    {
        try
        {
            ulong steamId = ServerAccessBook.TicketSteamId(authTicket, authTicketLength);
            eServerAccess access = _access.Decide(steamId.ToString());

            if (access is eServerAccess.Unknown or eServerAccess.Open)
            {
                return (access, nint.Zero);
            }

            nint password = access == eServerAccess.Allowed ? ServerPassword() : nint.Zero;

            _logger.LogInformation(
                "connect {steamId} '{name}': {access} by access list {version} | password swapped: {swapped}",
                steamId,
                Marshal.PtrToStringUTF8(name) ?? "",
                access,
                _access.Snapshot().Version,
                password != nint.Zero
            );

            return (access, password);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "access check failed; leaving the connect to the engine");

            return (eServerAccess.Unknown, nint.Zero);
        }
    }

    // Read at connect time: the owner can change sv_password over RCON while
    // the server hibernates, when nothing on a tick would notice.
    private nint ServerPassword()
    {
        string password = Core.ConVar.Find<string>("sv_password")?.Value ?? "";

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
