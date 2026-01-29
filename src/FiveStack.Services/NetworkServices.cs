using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

public class INetworkServerService : NativeObject
{
    private readonly VirtualFunctionWithReturn<nint, nint> GetIGameServerFunc;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr GetAddonNameDelegate(IntPtr self);

    public INetworkServerService()
        : base(NativeAPI.GetValveInterface(0, "NetworkServerService_001"))
    {
        int offset = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 23 : 24;
        GetIGameServerFunc = new VirtualFunctionWithReturn<nint, nint>(Handle, offset);
    }

    public string GetWorkshopID()
    {
        IntPtr networkGameServer = GetIGameServerFunc.Invoke(Handle);
        IntPtr vtablePtr = Marshal.ReadIntPtr(networkGameServer);
        IntPtr functionPtr = Marshal.ReadIntPtr(vtablePtr + (26 * IntPtr.Size));
        var getAddonName = Marshal.GetDelegateForFunctionPointer<GetAddonNameDelegate>(functionPtr);
        IntPtr result = getAddonName(networkGameServer);
        return Marshal.PtrToStringAnsi(result)!;
    }
}
