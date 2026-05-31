using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>P/Invoke-обёртки: перемещение в Корзину через оболочку Windows.</summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;       // ключевой флаг — отправить в Корзину
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>Переместить файл в Корзину Windows. Возвращает true при успехе.</summary>
    public static bool MoveToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // список путей завершается двойным нулём
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }
}
