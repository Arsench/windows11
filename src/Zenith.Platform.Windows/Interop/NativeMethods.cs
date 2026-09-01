using System.Runtime.InteropServices;

namespace Zenith.Platform.Windows.Interop;

/// <summary>
/// P/Invoke de bajo nivel. Se usan APIs nativas en lugar de contadores de
/// rendimiento porque los nombres de los contadores están traducidos en cada
/// idioma de Windows: en un Windows en español "Processor Information" no existe.
/// </summary>
internal static class NativeMethods
{
    internal const int SystemProcessorPerformanceInformation = 8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessorPerformance
    {
        internal long IdleTime;

        /// <summary>Incluye el tiempo de inactividad.</summary>
        internal long KernelTime;

        internal long UserTime;
        internal long DpcTime;
        internal long InterruptTime;
        internal uint InterruptCount;
    }

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPowerInformation
    {
        internal uint Number;
        internal uint MaxMhz;
        internal uint CurrentMhz;
        internal uint MhzLimit;
        internal uint MaxIdleState;
        internal uint CurrentIdleState;
    }

    internal const int ProcessorInformation = 11;

    [DllImport("powrprof.dll")]
    internal static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhys;
        internal ulong AvailPhys;
        internal ulong TotalPageFile;
        internal ulong AvailPageFile;
        internal ulong TotalVirtual;
        internal ulong AvailVirtual;
        internal ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PerformanceInformation
    {
        internal uint cb;
        internal IntPtr CommitTotal;
        internal IntPtr CommitLimit;
        internal IntPtr CommitPeak;
        internal IntPtr PhysicalTotal;
        internal IntPtr PhysicalAvailable;
        internal IntPtr SystemCache;
        internal IntPtr KernelTotal;
        internal IntPtr KernelPaged;
        internal IntPtr KernelNonpaged;
        internal IntPtr PageSize;
        internal uint HandleCount;
        internal uint ProcessCount;
        internal uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

    // ---- Identidad física de archivos (detección de vínculos duros) ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal long CreationTime;
        internal long LastAccessTime;
        internal long LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        out ByHandleFileInformation information);

    // ---- Papelera de reciclaje ----------------------------------------------

    internal const uint FO_MOVE = 0x0001;
    internal const uint FO_DELETE = 0x0003;

    internal const ushort FOF_SILENT = 0x0004;
    internal const ushort FOF_NOCONFIRMATION = 0x0010;
    internal const ushort FOF_ALLOWUNDO = 0x0040;
    internal const ushort FOF_NOERRORUI = 0x0400;
    internal const ushort FOF_NOCONFIRMMKDIR = 0x0200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShFileOpStruct
    {
        internal IntPtr hwnd;
        internal uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] internal string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? pTo;
        internal ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] internal bool fAnyOperationsAborted;
        internal IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
