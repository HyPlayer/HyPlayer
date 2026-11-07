using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HyPlayer.Features.Playback.Models;

public sealed record GpuAdapter(int Index, string Name, ulong DedicatedMemory);

public static unsafe partial class GpuAdapters
{
    private const uint SoftwareAdapterFlag = 2;
    private static readonly uint[] HardwareVendors = [0x10DE, 0x1002, 0x1022, 0x8086, 0x5143, 0x4D4F4351];
    private static readonly Guid FactoryInterface = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static IReadOnlyList<GpuAdapter> Enumerate()
    {
        var result = new List<GpuAdapter>();
        var seen = new HashSet<(uint, uint, uint)>();
        IDXGIFactory1 factory;
        try
        {
            if (CreateDXGIFactory1(FactoryInterface, out factory) < 0 || factory is null) return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return result;
        }

        for (uint i = 0; i < 16; i++)
        {
            if (factory.EnumAdapters1(i, out var adapter) < 0 || adapter is null) break;
            AdapterDescription description;
            if (adapter.GetDesc1(&description) < 0) continue;
            if ((description.Flags & SoftwareAdapterFlag) != 0) continue;
            if (Array.IndexOf(HardwareVendors, description.VendorId) < 0) continue;
            if (!seen.Add((description.VendorId, description.DeviceId, description.SubSysId))) continue;
            var name = new string(description.Description).TrimEnd('\0').Trim();
            result.Add(new GpuAdapter((int)i, name, description.DedicatedVideoMemory));
        }

        return result;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out IDXGIFactory1 factory);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdapterDescription
{
    public fixed char Description[128];
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public uint LuidLow;
    public int LuidHigh;
    public uint Flags;
}

[GeneratedComInterface]
[Guid("aec22fb8-76f3-4639-9be0-28eb43a67a2e")]
internal unsafe partial interface IDXGIObject
{
    [PreserveSig]
    int SetPrivateData(in Guid name, uint size, void* data);

    [PreserveSig]
    int SetPrivateDataInterface(in Guid name, nint unknown);

    [PreserveSig]
    int GetPrivateData(in Guid name, uint* size, void* data);

    [PreserveSig]
    int GetParent(in Guid riid, void** parent);
}

[GeneratedComInterface]
[Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
internal unsafe partial interface IDXGIFactory : IDXGIObject
{
    [PreserveSig]
    int EnumAdapters(uint index, nint* adapter);

    [PreserveSig]
    int MakeWindowAssociation(nint window, uint flags);

    [PreserveSig]
    int GetWindowAssociation(nint* window);

    [PreserveSig]
    int CreateSwapChain(nint device, void* description, nint* swapChain);

    [PreserveSig]
    int CreateSoftwareAdapter(nint module, nint* adapter);
}

[GeneratedComInterface]
[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
internal unsafe partial interface IDXGIFactory1 : IDXGIFactory
{
    [PreserveSig]
    int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);

    [PreserveSig]
    int IsCurrent();
}

[GeneratedComInterface]
[Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
internal unsafe partial interface IDXGIAdapter : IDXGIObject
{
    [PreserveSig]
    int EnumOutputs(uint output, nint* value);

    [PreserveSig]
    int GetDesc(void* description);

    [PreserveSig]
    int CheckInterfaceSupport(in Guid name, long* version);
}

[GeneratedComInterface]
[Guid("29038f61-3839-4626-91fd-086879011a05")]
internal unsafe partial interface IDXGIAdapter1 : IDXGIAdapter
{
    [PreserveSig]
    int GetDesc1(AdapterDescription* description);
}
