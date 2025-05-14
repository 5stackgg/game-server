using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public static class DynamicHookExtensions
{
    // TODO - pseudo safe
    public static Span<T> GetParamArray<T>(
        this DynamicHook hook,
        int paramIndex,
        int lengthParamIndex
    )
        where T : struct
    {
        var value = hook.GetParam<nint>(paramIndex);
        var length = hook.GetParam<int>(lengthParamIndex);
        var array = new T[length];
        for (int i = 0; i < length; i++)
        {
            array[i] = Marshal.PtrToStructure<T>(value + (i * Marshal.SizeOf<T>()));
        }
        return array;
    }
}

public partial class FiveStackPlugin
{
    // near "CNetworkGameServerBase::ConnectClient( name=\'%s\', remote=\'%s\' )\n"
    private static string ConnectClientSignature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "55 48 89 E5 41 57 49 89 F7 41 56 4D 89 CE 41 55 49 89 FD 41 54 48 8D 7D 80 49 89 D4 53 48 81 EC F8 00 00 00 8B 45 20 89 8D 10 FF FF FF B9 08 00 00 00 8B 55 18 48 89 BD 28 FF FF FF 48 8B 75 10 4C 89 85 18 FF FF FF 89 85 20 FF FF FF"
        : "48 89 5C 24 ? 44 89 4C 24 ? 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24";

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
        var authTicket = hook.GetParamArray<byte>(6, 7);
        var steamId = MemoryMarshal.Read<ulong>(authTicket[..8]);

        _logger.LogInformation($"init connect steam id: {steamId}");

        return HookResult.Continue;
    }
}
