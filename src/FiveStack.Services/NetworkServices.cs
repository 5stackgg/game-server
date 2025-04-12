using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

[StructLayout(LayoutKind.Sequential)]
struct CUtlMemory
{
    public unsafe nint* m_pMemory;
    public int m_nAllocationCount;
    public int m_nGrowSize;
}

[StructLayout(LayoutKind.Sequential)]
struct CUtlVector
{
    public unsafe nint this[int index]
    {
        get => this.m_Memory.m_pMemory[index];
        set => this.m_Memory.m_pMemory[index] = value;
    }

    public int m_iSize;
    public CUtlMemory m_Memory;

    public nint Element(int index) => this[index];
}

public class INetworkServerService : NativeObject
{
    private readonly VirtualFunctionWithReturn<nint, nint> GetIGameServerFunc;

    public INetworkServerService()
        : base(NativeAPI.GetValveInterface(0, "NetworkServerService_001"))
    {
        int offset = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 23 : 24;
        this.GetIGameServerFunc = new VirtualFunctionWithReturn<nint, nint>(this.Handle, offset);
    }

    public INetworkGameServer GetIGameServer()
    {
        return new INetworkGameServer(this.GetIGameServerFunc.Invoke(this.Handle));
    }
}

public class INetworkGameServer : NativeObject
{
    private static int SlotsOffset = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? 624
        : 640;

    private CUtlVector Slots;

    public INetworkGameServer(nint ptr)
        : base(ptr)
    {
        this.Slots = Marshal.PtrToStructure<CUtlVector>(base.Handle + SlotsOffset);
    }
}
