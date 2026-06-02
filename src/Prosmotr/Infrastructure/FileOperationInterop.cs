using System;
using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>COM Interop для IFileOperation — современного API удаления файлов (Vista+),
/// рекомендованного Microsoft взамен SHFileOperation. Ключевой флаг FOFX_NOCOPYHOOKS
/// пропускает shell extensions (TortoiseGit, антивирусы и т.д.), которые часто
/// вызывают зависания SHFileOperation при серийном удалении.
/// </summary>
internal static class FileOperationInterop
{
    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndParent);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems(IntPtr psiItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr psiItemDlg);
        void RenameItems(IntPtr psiItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr psiItemDlg);
        void MoveItems(IntPtr psiItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IntPtr psiItemDlg);
        void CopyItems(IntPtr psiItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IntPtr psiItemDlg);
        void DeleteItems(IntPtr psiItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IntPtr psiItemDlg);
        [PreserveSig] uint PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    internal static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    internal static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    // FOF flags (shellapi.h)
    internal const uint FOF_ALLOWUNDO         = 0x0040; // в Корзину
    internal const uint FOF_NOCONFIRMATION    = 0x0010; // без подтверждения
    internal const uint FOF_SILENT            = 0x0004; // без UI прогресса
    internal const uint FOF_NOERRORUI         = 0x0400; // без окна ошибок
    internal const uint FOF_WANTNUKEWARNING   = 0x4000;

    // FOFX flags (shobjidl.h)
    internal const uint FOFX_NOSKIPJUNCTIONS        = 0x00010000;
    internal const uint FOFX_PREFERHARDLINK         = 0x00020000;
    internal const uint FOFX_SHOWELEVATIONPROMPT    = 0x00040000;
    internal const uint FOFX_RECYCLEONDELETE        = 0x00080000; // Vista fallback
    internal const uint FOFX_EARLYFAILURE           = 0x00100000;
    internal const uint FOFX_PRESERVEFILEEXTENSIONS = 0x00200000;
    internal const uint FOFX_KEEPNEWERFILE          = 0x00400000;
    internal const uint FOFX_NOCOPYHOOKS            = 0x00800000; // <— пропускаем buggy copy hooks!
    internal const uint FOFX_NOMINIMIZEBOX          = 0x01000000;
    internal const uint FOFX_MOVEACLSACROSSVOLUMES  = 0x02000000;
    internal const uint FOFX_DONTDISPLAYSOURCEPATH  = 0x04000000;
    internal const uint FOFX_DONTDISPLAYDESTPATH    = 0x08000000;
    internal const uint FOFX_REQUIREELEVATION       = 0x10000000;
    internal const uint FOFX_ADDUNDORECORD          = 0x20000000;
    internal const uint FOFX_COPYASDOWNLOAD         = 0x40000000;
    internal const uint FOFX_DONTDISPLAYLOCATIONS    = 0x80000000;
}
