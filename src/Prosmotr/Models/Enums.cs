namespace Prosmotr.Models;

/// <summary>Режим вписывания изображения в область просмотра.</summary>
public enum ImageViewMode
{
    Fit,        // по размеру окна (вписать целиком)
    ActualSize, // 100%
    Fill        // заполнить область (по большей стороне)
}

/// <summary>Тема оформления.</summary>
public enum AppTheme
{
    System, // следовать системной теме Windows
    Light,
    Dark
}

/// <summary>Расположение ленты миниатюр.</summary>
public enum ThumbnailStripPosition
{
    Bottom,
    Left
}

/// <summary>Поле сортировки галереи.</summary>
public enum SortField
{
    Name,
    DateModified,
    DateCreated,
    Size,
    Type
}

/// <summary>Параметры сортировки: поле + направление.</summary>
public readonly record struct SortSpec(SortField Field, bool Descending);
