using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>Натуральная сортировка имён файлов «как в Проводнике» (file2 раньше file10).</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
}
