using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HyPlayer.Features.Playback.Models;

public sealed record GpuAdapter(int Index, string Name, ulong DedicatedMemory);

public static unsafe class GpuAdapters
{
    private const uint SoftwareAdapterFlag = 2;
    private static readonly uint[] HardwareVendors = [0x10DE, 0x1002, 0x1022, 0x8086, 0x5143, 0x4D4F4351];

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDescription
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

    public static IReadOnlyList<GpuAdapter> Enumerate()
    {
        var result = new List<GpuAdapter>();
        var seen = new HashSet<(uint, uint, uint)>();
        if (!NativeLibrary.TryLoad("dxgi.dll", out var library)) return result;
        if (!NativeLibrary.TryGetExport(library, "CreateDXGIFactory1", out var create)) return result;
        var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        void* factory = null;
        if (((delegate* unmanaged[Stdcall]<Guid*, void**, int>)create)(&factoryId, &factory) < 0 || factory == null) return result;
        try
        {
            var factoryTable = *(void***)factory;
            for (uint i = 0; i < 16; i++)
            {
                void* adapter = null;
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)factoryTable[12])(factory, i, &adapter) < 0 || adapter == null) break;
                try
                {
                    var adapterTable = *(void***)adapter;
                    AdapterDescription description;
                    if (((delegate* unmanaged[Stdcall]<void*, AdapterDescription*, int>)adapterTable[10])(adapter, &description) < 0) continue;
                    if ((description.Flags & SoftwareAdapterFlag) != 0) continue;
                    if (Array.IndexOf(HardwareVendors, description.VendorId) < 0) continue;
                    if (!seen.Add((description.VendorId, description.DeviceId, description.SubSysId))) continue;
                    var name = new string(description.Description).TrimEnd('\0').Trim();
                    result.Add(new GpuAdapter((int)i, name, description.DedicatedVideoMemory));
                }
                finally
                {
                    ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)adapter)[2])(adapter);
                }
            }
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)factory)[2])(factory);
        }
        return result;
    }
}
