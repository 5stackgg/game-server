using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

public class INetworkServerService : NativeObject
{
    private readonly VirtualFunctionWithReturn<nint, nint> GetIGameServerFunc;

    public INetworkServerService()
        : base(NativeAPI.GetValveInterface(0, "NetworkServerService_001"))
    {
        int offset = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 23 : 24;
        GetIGameServerFunc = new VirtualFunctionWithReturn<nint, nint>(Handle, offset);
    }

    public nint GetIGameServerHandle()
    {
        return GetIGameServerFunc.Invoke(Handle);
    }
}
