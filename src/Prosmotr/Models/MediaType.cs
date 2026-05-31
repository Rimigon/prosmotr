namespace Prosmotr.Models;

/// <summary>Тип медиафайла, определяющий способ отображения.</summary>
public enum MediaType
{
    Unknown,
    Image,
    AnimatedImage, // анимированный GIF — рендерим через XamlAnimatedGif
    Video
}
