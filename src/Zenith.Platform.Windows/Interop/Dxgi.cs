using System.Runtime.InteropServices;

namespace Zenith.Platform.Windows.Interop;

/// <summary>
/// Enumeración de adaptadores gráficos vía DXGI. Es la fuente correcta: da el
/// LUID (necesario para casar con los contadores de GPU de Windows) y la VRAM
/// real. <c>Win32_VideoController.AdapterRAM</c> es un uint32 y miente por
/// encima de 4 GB, así que no se usa.
/// </summary>
internal static class Dxgi
{
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    internal sealed record AdapterDescription(string Name, string Luid, long DedicatedVideoMemory);

    internal static IReadOnlyList<AdapterDescription> EnumerateAdapters()
    {
        var factoryGuid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        var results = new List<AdapterDescription>();

        IDXGIFactory1? factory = null;
        try
        {
            if (CreateDXGIFactory1(ref factoryGuid, out factory) != 0 || factory is null) return results;

            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out var adapter) != 0 || adapter is null) break;

                try
                {
                    if (adapter.GetDesc1(out var desc) != 0) continue;
                    if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue; // Adaptador software de Microsoft.

                    var vram = (ulong)desc.DedicatedVideoMemory;
                    // Nombre vacío si DXGI no lo da: el texto de relleno lo pone la interfaz, traducido.
                    var name = desc.Description is { Length: > 0 } d ? d.Trim() : string.Empty;

                    results.Add(new AdapterDescription(
                        name,
                        FormatLuid(desc.AdapterLuidHigh, desc.AdapterLuidLow),
                        vram > (ulong)long.MaxValue ? long.MaxValue : (long)vram));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        catch (Exception)
        {
            // DXGI puede no estar disponible (sesión sin escritorio, contenedor…).
            // El proveedor de GPU tiene un camino alternativo por WMI.
        }
        finally
        {
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }

        return results;
    }

    /// <summary>
    /// Formato del LUID tal y como aparece en las instancias de los contadores
    /// de GPU: <c>luid_0xHIGH_0xLOW</c>.
    /// </summary>
    internal static string FormatLuid(int high, uint low) =>
        $"luid_0x{high:X8}_0x{low:X8}";

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1? factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    // Los métodos que no usamos se declaran igualmente: la vtable COM se resuelve
    // por posición, así que el orden y el número de entradas debe ser exacto.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig] int GetDesc1(out DxgiAdapterDesc1 description);
    }
}
