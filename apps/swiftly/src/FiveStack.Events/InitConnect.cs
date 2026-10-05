using System.Runtime.InteropServices;
using System.Text;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;

namespace FiveStack;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate nint ConnectClientDelegate(
    nint param1,
    nint param2,
    nint param3,
    nint param4,
    nint param5,
    nint param6,
    nint param7,
    int param8,
    bool param9
);

public partial class FiveStackPlugin
{
    private static int PasswordBufferLength = 86;
    public static nint PasswordBuffer { get; set; } = nint.Zero;
    public static Dictionary<ulong, string> PendingPlayers = new();

    /**
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
    private const string ConnectClientSignature = "FiveStack_ConnectClient";

    private IUnmanagedFunction<ConnectClientDelegate>? _connectClientFunc;
    private Guid _connectClientHookId;

    private void InitializeConnectClientHook()
    {
        try
        {
            if (_connectClientFunc != null)
            {
                return;
            }

            if (!Core.GameData.TryGetSignature(ConnectClientSignature, out nint address))
            {
                _logger.LogWarning("Failed to find ConnectClient signature");
                return;
            }

            _connectClientFunc = Core.Memory.GetUnmanagedFunctionByAddress<ConnectClientDelegate>(
                address
            );

            if (_connectClientFunc == null)
            {
                _logger.LogWarning("Failed to get unmanaged function for ConnectClient");
                return;
            }

            _connectClientHookId = _connectClientFunc.AddHook(
                (next) =>
                {
                    return (
                        nint param1,
                        nint param2,
                        nint param3,
                        nint param4,
                        nint param5,
                        nint param6,
                        nint param7,
                        int param8,
                        bool param9
                    ) =>
                    {
                        var name = Marshal.PtrToStringUTF8(param2) ?? "";
                        var token = Marshal.PtrToStringUTF8(param6);

                        ulong steamId = 0;
                        unsafe
                        {
                            if (param7 != nint.Zero && param8 >= 8)
                            {
                                var authTicket = new Span<byte>((byte*)param7, param8);
                                steamId = MemoryMarshal.Read<ulong>(authTicket[..8]);
                            }
                        }

                        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

                        ConnectRules? rules =
                            match == null
                                ? null
                                : MatchUtility.GetConnectRules(match, steamId, name);

                        ConnectDecision decision = ConnectUtility.Authorize(rules, steamId, token);

                        if (decision.pending_role != null)
                        {
                            PendingPlayers[steamId] = decision.pending_role;
                        }

                        _logger.LogInformation(
                            "{connect}",
                            ConnectUtility.Describe(
                                rules,
                                decision,
                                steamId,
                                name,
                                token,
                                param8,
                                PasswordBuffer != nint.Zero
                            )
                        );

                        if (
                            decision.action == eConnectAction.Authorized
                            && PasswordBuffer != nint.Zero
                        )
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                PasswordBuffer,
                                param7,
                                param8,
                                param9
                            );
                        }

                        if (decision.action == eConnectAction.Reject)
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                nint.Zero,
                                0,
                                param9
                            );
                        }

                        return next()(
                            param1,
                            param2,
                            param3,
                            param4,
                            param5,
                            param6,
                            param7,
                            param8,
                            param9
                        );
                    };
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ConnectClient hook");
        }
    }

    private void UninstallConnectClientHook()
    {
        try
        {
            if (_connectClientFunc != null && _connectClientHookId != Guid.Empty)
            {
                _connectClientFunc.RemoveHook(_connectClientHookId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ConnectClient hook");
        }

        _connectClientFunc = null;
        _connectClientHookId = Guid.Empty;

        if (PasswordBuffer != nint.Zero)
        {
            Marshal.FreeCoTaskMem(PasswordBuffer);
            PasswordBuffer = nint.Zero;
        }
    }

    public static void SetPasswordBuffer(string password)
    {
        PasswordBuffer = Marshal.StringToCoTaskMemUTF8(new string('\0', PasswordBufferLength));
        StrCpy(PasswordBuffer, password);
    }

    private static unsafe void StrCpy(nint dst, string src)
    {
        Span<byte> buffer = stackalloc byte[PasswordBufferLength];

        int length = Encoding.UTF8.GetBytes(src, buffer[..(buffer.Length - 1)]);
        buffer[length] = (byte)'\0';

        var dstBuffer = new Span<byte>((byte*)dst, PasswordBufferLength);
        buffer.CopyTo(dstBuffer);
    }
}
