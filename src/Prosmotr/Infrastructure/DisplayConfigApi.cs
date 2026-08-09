using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>
/// P/Invoke для CCD (Connecting and Configuring Displays) API.
/// Используется для переключения топологии дисплеев Windows (clone / extend).
/// </summary>
public static class DisplayConfigApi
{
    // --- Флаги SetDisplayConfig ---
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    // Сохраняет применённую конфигурацию в persistence database Windows (иначе при
    // перезагрузке/сне система восстановит последнюю СОХРАНЁННУЮ топологию — клон).
    // Валиден только в комбинации с SDC_USE_SUPPLIED_DISPLAY_CONFIG (см. MSDN).
    public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;
    public const uint SDC_PATH_PERSIST_IF_REQUIRED = 0x00000800;
    public const uint SDC_VIRTUAL_MODE_AWARE = 0x00008000;

    // --- Win32 error codes ---
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    // --- Флаги QueryDisplayConfig ---
    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const uint QDC_DATABASE_CURRENT = 0x00000004;
    public const uint QDC_VIRTUAL_MODE_AWARE = 0x00000010;

    // --- Флаги путей ---
    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;
    public const uint DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID = 0xffffffff;
    public const uint DISPLAYCONFIG_PATH_TARGET_MODE_IDX_INVALID = 0xffffffff;

    // --- System metrics ---
    public const int SM_CMONITORS = 80;

    // --- Structs ---

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        [FieldOffset(0)] public ulong pixelRate;
        [FieldOffset(8)] public DISPLAYCONFIG_RATIONAL hSyncFreq;
        [FieldOffset(16)] public DISPLAYCONFIG_RATIONAL vSyncFreq;
        [FieldOffset(24)] public DISPLAYCONFIG_2DREGION activeSize;
        [FieldOffset(32)] public DISPLAYCONFIG_2DREGION totalSize;
        [FieldOffset(40)] public uint videoStandard;
        [FieldOffset(44)] public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct DISPLAYCONFIG_TARGET_MODE
    {
        [FieldOffset(0)] public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct DISPLAYCONFIG_SOURCE_MODE
    {
        [FieldOffset(0)] public uint width;
        [FieldOffset(4)] public uint height;
        [FieldOffset(8)] public uint pixelFormat;
        [FieldOffset(12)] public POINTL position;
        [FieldOffset(20)] public uint cloneGroupId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        [FieldOffset(0)] public LUID adapterId;
        [FieldOffset(8)] public uint id;
        [FieldOffset(12)] public uint modeInfoIdx;
        [FieldOffset(16)] public uint statusFlags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        [FieldOffset(0)] public LUID adapterId;
        [FieldOffset(8)] public uint id;
        [FieldOffset(12)] public uint modeInfoIdx;
        [FieldOffset(16)] public uint outputTechnology;
        [FieldOffset(20)] public uint rotation;
        [FieldOffset(24)] public uint scaling;
        [FieldOffset(28)] public DISPLAYCONFIG_RATIONAL refreshRate;
        [FieldOffset(36)] public uint scanLineOrdering;
        [FieldOffset(40)] public int targetAvailable;
        [FieldOffset(44)] public uint statusFlags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        [FieldOffset(0)] public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        [FieldOffset(20)] public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        [FieldOffset(68)] public uint flags;
    }

    public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3,
    }

    // --- Functions ---

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
}
