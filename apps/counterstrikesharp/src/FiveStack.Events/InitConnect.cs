using System.Runtime.InteropServices;
using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private static int PasswordBufferLength = 86;
    public static nint PasswordBuffer { get; set; } = nint.Zero;
    public static Dictionary<ulong, string> PendingPlayers = new();

    // near "CNetworkGameServerBase::ConnectClient( name=\'%s\', remote=\'%s\' )\n"
    private static string ConnectClientSignature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "55 48 89 E5 41 57 49 89 D7 41 56 49 89 FE 41 55 41 54 53 89 CB 48 81 EC ? ? ? ?"
        : "48 89 5C 24 18 44 89 4C 24 20 55 41 54 41 55 41 56 41 57 48 8D 6C 24 F1 48 81 EC ? ? ? ? 81 64 24 54 FF FF 0F FF";

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
    /// </summary>
    public static MemoryFunctionWithReturn<
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
    > ConnectClientFunc = new(ConnectClientSignature, Addresses.EnginePath);
    public static Func<nint, nint, nint, nint, nint, nint, nint, int, bool, nint> ConnectClient =
        ConnectClientFunc.Invoke;

    private HookResult ConnectClientHook(DynamicHook hook)
    {
        var name = hook.GetParam<string>(1) ?? "";
        var authTicket = hook.GetParamArray<byte>(6, 7);
        var token = hook.GetParam<string>(5);
        ulong steamId = authTicket.Length >= 8 ? MemoryMarshal.Read<ulong>(authTicket[..8]) : 0;

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        ConnectRules? rules =
            match == null ? null : MatchUtility.GetConnectRules(match, steamId, name);

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
                authTicket.Length,
                PasswordBuffer != nint.Zero
            )
        );

        switch (decision.action)
        {
            case eConnectAction.Authorized:
                if (PasswordBuffer != nint.Zero)
                {
                    hook.SetParam(5, PasswordBuffer);
                }
                break;
            case eConnectAction.Reject:
                hook.SetParam(6, 0);
                hook.SetParam(7, 0);
                break;
        }

        return HookResult.Continue;
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

public static class DynamicHookExtensions
{
    public static unsafe Span<T> GetParamArray<T>(
        this DynamicHook hook,
        int paramIndex,
        int lengthParamIndex
    )
    {
        var value = hook.GetParam<nint>(paramIndex);
        var length = hook.GetParam<int>(lengthParamIndex);
        return new Span<T>((void*)value, length);
    }
}
