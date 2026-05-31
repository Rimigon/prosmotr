using System.IO;
using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Берёт РЕАЛЬНЫЙ порядок элементов из открытого окна Проводника для папки
/// (через Shell COM: ShellWindows → IShellBrowser → IFolderView2.Items в порядке отображения).
/// Это точно повторяет любую сортировку Windows (имя, дата, размер, длительность, дата съёмки и т.д.),
/// не пытаясь угадать колонку. Любая ошибка → false. Вызывать из STA-потока (UI).
/// </summary>
public static class ExplorerSortReader
{
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IFolderView2 = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid IID_IShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    private const uint SVGIO_ALLVIEW = 0x00000002;
    private const uint SVGIO_FLAG_VIEWORDER = 0x80000000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>Получить пути файлов в порядке отображения окна Проводника для папки.</summary>
    public static bool TryGetOrderedPaths(string folderPath, out List<string> paths)
    {
        paths = new List<string>();
        if (string.IsNullOrEmpty(folderPath)) return false;

        object? shellWindows = null;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (type == null) return false;
            shellWindows = Activator.CreateInstance(type);
            if (shellWindows == null) return false;

            dynamic windows = shellWindows;
            int count = windows.Count;
            string target = NormalizePath(folderPath);

            for (int i = 0; i < count; i++)
            {
                object? win = null;
                try { win = windows.Item(i); }
                catch { continue; }
                if (win == null) continue;

                try
                {
                    string? winPath = null;
                    try { winPath = ((dynamic)win).Document?.Folder?.Self?.Path as string; } catch { }
                    if (string.IsNullOrEmpty(winPath))
                    {
                        try { winPath = UrlToPath(((dynamic)win).LocationURL as string); } catch { }
                    }

                    if (!string.IsNullOrEmpty(winPath) && PathEquals(winPath, target))
                    {
                        if (TryReadOrder(win, paths) && paths.Count > 0)
                            return true;
                        paths.Clear();
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(win);
                }
            }
        }
        catch
        {
            // Shell COM недоступен или иная ошибка — используем сортировку приложения
        }
        finally
        {
            if (shellWindows != null) Marshal.ReleaseComObject(shellWindows);
        }

        return false;
    }

    private static bool TryReadOrder(object window, List<string> paths)
    {
        if (window is not IServiceProvider sp) return false;

        var sid = SID_STopLevelBrowser;
        var iidBrowser = IID_IShellBrowser;
        if (sp.QueryService(ref sid, ref iidBrowser, out var pBrowser) != 0 || pBrowser == IntPtr.Zero)
            return false;

        IShellBrowser? browser = null;
        IntPtr pView = IntPtr.Zero;
        IntPtr pFolderView = IntPtr.Zero;
        IFolderView2? folderView = null;
        IShellItemArray? items = null;
        try
        {
            browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
            if (browser.QueryActiveShellView(out pView) != 0 || pView == IntPtr.Zero)
                return false;

            var iidFv2 = IID_IFolderView2;
            if (Marshal.QueryInterface(pView, ref iidFv2, out pFolderView) != 0 || pFolderView == IntPtr.Zero)
                return false;

            folderView = (IFolderView2)Marshal.GetObjectForIUnknown(pFolderView);

            var iidArray = IID_IShellItemArray;
            if (folderView.Items(SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER, ref iidArray, out items) != 0 || items == null)
                return false;

            if (items.GetCount(out uint n) != 0) return false;
            for (uint k = 0; k < n; k++)
            {
                if (items.GetItemAt(k, out var item) != 0 || item == null) continue;
                try
                {
                    if (item.GetDisplayName(SIGDN_FILESYSPATH, out var pName) == 0 && pName != IntPtr.Zero)
                    {
                        var s = Marshal.PtrToStringUni(pName);
                        Marshal.FreeCoTaskMem(pName);
                        if (!string.IsNullOrEmpty(s)) paths.Add(s!);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }

            return paths.Count > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (items != null) Marshal.ReleaseComObject(items);
            if (folderView != null) Marshal.ReleaseComObject(folderView);
            if (pFolderView != IntPtr.Zero) Marshal.Release(pFolderView);
            if (pView != IntPtr.Zero) Marshal.Release(pView);
            if (browser != null) Marshal.ReleaseComObject(browser);
            if (pBrowser != IntPtr.Zero) Marshal.Release(pBrowser);
        }
    }

    private static string? UrlToPath(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return new Uri(url).LocalPath;
        }
        catch { }
        return null;
    }

    private static string NormalizePath(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        return path.TrimEnd('\\', '/');
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

    // --- COM-интерфейсы (порядок методов = раскладка vtable, не менять!) ---

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IntPtr ppshv);
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        void GetCurrentViewMode();  // 1
        void SetCurrentViewMode();  // 2
        void GetFolder();           // 3
        void Item();                // 4
        void ItemCount();           // 5
        [PreserveSig]
        int Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv); // 6
        // остальные методы не нужны
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler();               // 1
        void GetPropertyStore();            // 2
        void GetPropertyDescriptionList();  // 3
        void GetAttributes();               // 4
        [PreserveSig] int GetCount(out uint pdwNumItems);                                          // 5
        [PreserveSig] int GetItemAt(uint dwIndex, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi); // 6
        // EnumItems не нужен
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();  // 1
        void GetParent();      // 2
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);  // 3
        // GetAttributes, Compare не нужны
    }
}
