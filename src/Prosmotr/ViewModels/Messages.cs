namespace Prosmotr.ViewModels;

/// <summary>Запрос переключения полноэкранного режима (из области видео по двойному клику).</summary>
public sealed record ToggleFullScreenMessage;

/// <summary>Запрос перехода к другому файлу из области видео (Direction: -1 назад, +1 вперёд).</summary>
public sealed record NavigateFileMessage(int Direction);

/// <summary>Запрос переключения видимости элементов управления (панель, стрелки) для видео.</summary>
public sealed record ToggleChromeMessage;

/// <summary>Запрос переключения режима Picture-in-Picture для текущего видео.</summary>
public sealed record TogglePictureInPictureMessage;
