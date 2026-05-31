using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prosmotr.Models;

/// <summary>Элемент ленты миниатюр (наблюдаемый — миниатюра грузится асинхронно).</summary>
public sealed partial class ThumbnailEntry : ObservableObject
{
    public ThumbnailEntry(MediaItem item)
    {
        Item = item;
    }

    public MediaItem Item { get; }

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsVideo => Item.IsVideo;
    public string FileName => Item.FileName;
}
