using System.IO;
using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>P/Invoke-обёртки: перемещение в Корзину через IFileOperation (Vista+).</summary>
internal static class NativeMethods
{
    /// <summary>Инициализировать COM в STA-потоке.</summary>
    [DllImport("ole32.dll")]
    internal static extern int OleInitialize(IntPtr pvReserved);

    /// <summary>Деинициализировать COM в STA-потоке.</summary>
    [DllImport("ole32.dll")]
    internal static extern void OleUninitialize();

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(IntPtr hwndParent);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr psiItem);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr punkItems);
        [PreserveSig] int DeleteItem(IntPtr psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems(IntPtr punkItems);
        [PreserveSig] int NewItem(IntPtr psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int CopyItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        [PreserveSig] int MoveItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        [PreserveSig] int RenameItem(IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int RenameItems(IntPtr punkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted(out bool pfAnyOperationsAborted);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    internal static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    internal static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    // IFileOperation flags
    internal const uint FOFX_NOCONFIRMATION = 0x00010;
    internal const uint FOFX_SILENT = 0x00004;
    internal const uint FOFX_NOERRORUI = 0x00400;
    internal const uint FOFX_RECYCLEONDELETE = 0x80000;
    internal const uint FOFX_EARLYFAILURE = 0x00100000;

    /// <summary>Переместить файл в Корзину через IFileOperation. Возвращает true при успехе.</summary>
    public static bool MoveToRecycleBin(string path)
    {
        if (!File.Exists(path)) return false;

        IFileOperation? fileOp = null;
        object? itemObj = null;
        IntPtr itemPtr = IntPtr.Zero;
        try
        {
            fileOp = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FileOperation))!;
            fileOp.SetOperationFlags(FOFX_NOCONFIRMATION | FOFX_SILENT | FOFX_NOERRORUI | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE);

            SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out itemObj);
            itemPtr = Marshal.GetIUnknownForObject(itemObj);

            fileOp.DeleteItem(itemPtr, IntPtr.Zero);
            fileOp.PerformOperations();
            fileOp.GetAnyOperationsAborted(out var aborted);
            return !aborted;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
            if (itemObj != null) Marshal.ReleaseComObject(itemObj);
            if (fileOp != null) Marshal.ReleaseComObject(fileOp);
        }
    }
}
