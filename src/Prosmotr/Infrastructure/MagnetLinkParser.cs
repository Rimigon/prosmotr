using System.Text.RegularExpressions;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Ленящая валидация магнет-ссылок для UI (диалог, буфер обмена, аргументы).
/// Авторитетная проверка — MonoTorrent.MagnetLink.TryParse в движке; здесь только
/// отсекаем очевидный мусор, чтобы не дёргать движок и не открывать диалоги зря.
/// Умышленно не требуем, чтобы xt был первым параметром и чтобы после хеша ничего
/// не было — ссылки с &dn=/&tr= должны проходить.
/// </summary>
public static partial class MagnetLinkParser
{
    [GeneratedRegex(@"^magnet:.*?xt=urn:btih:([0-9a-f]{32,40})", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetXtRegex();

    public static bool IsValidMagnet(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return MagnetXtRegex().IsMatch(input.Trim());
    }

    public static bool TryGetInfoHash(string? input, out string infoHash)
    {
        infoHash = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var match = MagnetXtRegex().Match(input.Trim());
        if (!match.Success) return false;
        infoHash = match.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    /// <summary>Отображаемое имя ссылки: параметр &amp;dn= (URL-декодированный), иначе — префикс infoHash.</summary>
    public static string GetDisplayName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Магнет-ссылка";
        var dn = Regex.Match(input, "[?&]dn=([^&]+)", RegexOptions.IgnoreCase);
        if (dn.Success)
        {
            try
            {
                var name = Uri.UnescapeDataString(dn.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { /* битый dn — fallback ниже */ }
        }
        return TryGetInfoHash(input, out var hash) && hash.Length >= 8
            ? hash[..8]
            : "Магнет-ссылка";
    }
}
