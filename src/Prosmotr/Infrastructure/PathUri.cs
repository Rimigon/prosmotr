namespace Prosmotr.Infrastructure;

/// <summary>
/// Построение абсолютного URI из локального пути, устойчивое к спецсимволам.
/// Пути с # и % ломают <c>new Uri(path)</c> (UriFormatException) — единое место
/// fallback-экранирования (раньше дублировалось в ImageViewerViewModel и VideoPlaybackService).
/// </summary>
public static class PathUri
{
    /// <summary>Экранировать путь для new Uri: сначала %, потом # (порядок важен,
    /// иначе уже вставленные %25 повторно экранируются).</summary>
    public static string Escape(string path) => path.Replace("%", "%25").Replace("#", "%23");

    /// <summary>Абсолютный URI из локального пути; при сбое — с экранированием # и %.</summary>
    public static Uri ToUri(string path)
    {
        try { return new Uri(path, UriKind.Absolute); }
        catch (UriFormatException) { return new Uri(Escape(path), UriKind.Absolute); }
    }
}
