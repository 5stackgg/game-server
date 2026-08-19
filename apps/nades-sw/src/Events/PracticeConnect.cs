using System.Runtime.InteropServices;
using System.Text;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;

namespace NadePractice;

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

// A practice server is not public. It never loads the match plugin, so the
// door is here: the same ConnectClient hook the match plugin uses, deciding
// against the practice session's roster instead of a match lineup.
public partial class NadePracticePlugin
{
    private static int PasswordBufferLength = 86;
    public static nint PasswordBuffer { get; set; } = nint.Zero;
    public static Dictionary<ulong, string> PendingPlayers = new();

    /**
     * Signature near:
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
    private static string ConnectClientSignature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "55 48 89 E5 41 57 49 89 D7 41 56 41 89 CE 41 55 41 54 49 89 F4 53 48 89 FB 48 81 EC ? ? ? ?"
        : "48 89 5C 24 18 44 89 4C 24 20 55 41 54 41 55 41 56 41 57 48 8D 6C 24 F1 48 81 EC ? ? ? ? 81 64 24 54 FF FF 0F FF";

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

            var address = Core.Memory.GetAddressBySignature(Library.Engine, ConnectClientSignature);

            if (address == null || address == nint.Zero)
            {
                _logger.LogWarning("Failed to find ConnectClient signature");
                return;
            }

            _connectClientFunc = Core.Memory.GetUnmanagedFunctionByAddress<ConnectClientDelegate>(
                address.Value
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

                        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
                            _session.Current,
                            steamId,
                            token
                        );

                        if (decision.pending_role != null)
                        {
                            PendingPlayers[steamId] = decision.pending_role;
                        }

                        if (
                            decision.action == ePracticeConnect.Authorized
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

                        if (decision.action == ePracticeConnect.Reject)
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
        if (PasswordBuffer == nint.Zero)
        {
            PasswordBuffer = Marshal.StringToCoTaskMemUTF8(new string('\0', PasswordBufferLength));
        }

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
