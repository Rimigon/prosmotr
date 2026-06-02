using System.IO;
using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

internal static class NativeMethods
{
    /// <summary>Инициализировать COM в STA-потоке.</summary>
    [DllImport("ole32.dll")]
    internal static extern int OleInitialize(IntPtr pvReserved);

    /// <summary>Деинициализировать COM в STA-потоке.</summary>
    [DllImport("ole32.dll")]
    internal static extern void OleUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    public static bool MoveToRecycleBinFallback(string path, string reason)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
            };
            int hr = SHFileOperation(ref op);
            AppLog.Write($"[MoveToRecycleBinFallback] SHFileOperation hr=0x{hr:X8}, aborted={op.fAnyOperationsAborted}, reason={reason}");
            return hr == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[MoveToRecycleBinFallback] exception: {ex}");
            return false;
        }
    }
}
