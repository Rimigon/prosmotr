namespace Prosmotr.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>Все пользовательские настройки, сохраняемые между запусками (JSON в %APPDATA%).</summary>
public sealed class AppSettings
{
    // --- Видео ---
    /// <summary>Глобальная скорость воспроизведения по умолчанию для всех новых видео.</summary>
    [Range(0.25, 4.0)]
    public float DefaultPlaybackRate { get; set; } = 1.0f;
    /// <summary>Запоминать последнюю скорость отдельно для каждого файла.</summary>
    public bool RememberRatePerFile { get; set; } = true;
    /// <summary>Запоминать выбранную аудиодорожку отдельно для каждого файла.</summary>
    public bool RememberAudioTrackPerFile { get; set; } = true;
    /// <summary>Запоминать выбранную аудиодорожку для ПАПКИ (сезона сериала): выбор озвучки
    /// в одной серии применяется ко всем файлам папки, у которых такая дорожка есть.
    /// Приоритетнее пер-файловой памяти (решение пользователя).</summary>
    public bool RememberAudioTrackPerFolder { get; set; } = true;
    /// <summary>Продолжать воспроизведение видео с сохранённой позиции.</summary>
    public bool ResumeVideoPosition { get; set; } = true;
    public int LastVolume { get; set; } = 100;
    public bool LastMuted { get; set; } = false;
    /// <summary>Шаг перемотки видео клавишами ←/→ в полноэкранном режиме, секунд.
    /// Границы [1, 30] согласованы со слайдером в настройках и Math.Clamp в SettingsViewModel.</summary>
    [Range(1, 30)]
    public int SeekStepSeconds { get; set; } = 5;
    /// <summary>Покадровая перемотка: ←/→ шагают по одному кадру вместо <see cref="SeekStepSeconds"/>.</summary>
    public bool FrameByFrameSeek { get; set; } = false;
    /// <summary>Стрелки ←/→ всегда перематывают открытое видео, а не переключают файлы (в любом режиме окна).</summary>
    public bool ArrowKeysSeekVideo { get; set; } = false;
    /// <summary>Показывать мини-таймлайн при скрытой панели управления видео.</summary>
    public bool ShowMiniTimeline { get; set; } = true;
    /// <summary>Видео короче этого порога (в минутах) показывает мини-таймлайн при скрытой панели.</summary>
    [Range(1, 120)]
    public int MiniTimelineThresholdMinutes { get; set; } = 20;
    /// <summary>Показывать превью кадра при наведении на таймлайн видео.</summary>
    public bool ShowTimelinePreview { get; set; } = true;
    /// <summary>Ставить видео на паузу при наведении на таймлайн (режим скраббинга).</summary>
    public bool TimelinePreviewPauseVideo { get; set; } = false;

    // --- Удаление ---
    public bool ConfirmDelete { get; set; } = true;
    /// <summary>Удалять навсегда вместо перемещения в Корзину.</summary>
    public bool PermanentDelete { get; set; } = false;
    /// <summary>Показывать плашку-уведомление после удаления файла.</summary>
    public bool ShowDeleteNotification { get; set; } = true;

    // --- Оформление ---
    public AppTheme Theme { get; set; } = AppTheme.System;

    // --- Поведение / интерфейс ---
    public bool OpenLastOnStartup { get; set; } = false;
    public bool ClearRecentOnStartup { get; set; } = false;
    public bool ShowThumbnails { get; set; } = true;
    public ThumbnailStripPosition ThumbnailStripPosition { get; set; } = ThumbnailStripPosition.Bottom;
    /// <summary>Автоскрытие панели управления в полноэкранном режиме.</summary>
    public bool AutoHideControls { get; set; } = true;

    // --- Слайд-шоу ---
    [Range(1, 60)]
    public int SlideshowIntervalSeconds { get; set; } = 4;

    // --- Сортировка ---
    public SortField SortBy { get; set; } = SortField.Name;
    public bool SortDescending { get; set; } = false;
    /// <summary>При открытии папки повторять порядок сортировки открытого окна Проводника.
    /// По умолчанию выключено: программа всегда применяет собственную сортировку (SortBy/SortDescending),
    /// сохранённую внутри приложения, а не порядок Проводника.</summary>
    public bool MatchExplorerSort { get; set; } = false;

    /// <summary>Явно выбранная пользователем сортировка по папкам (ключ — путь, значение — "Поле:Убыв").
    /// Имеет ВЫСШИЙ приоритет — перекрывает автоопределение из Проводника.</summary>
    public Dictionary<string, string> ManualFolderSorts { get; set; } = new();

    // --- Горячие клавиши ---
    /// <summary>Клавиша для закрытия программы (название из System.Windows.Input.Key).</summary>
    public string ExitKey { get; set; } = "End";
    /// <summary>Клавиша для скрытия/показа элементов управления (название из System.Windows.Input.Key).</summary>
    public string ToggleChromeKey { get; set; } = "PageDown";
    /// <summary>Клавиша для переключения полноэкранного режима (название из System.Windows.Input.Key).</summary>
    public string FullScreenKey { get; set; } = "F11";

    // --- Интеграция с Windows ---
    /// <summary>Регистрировать ассоциации и контекстное меню при запуске.</summary>
    public bool IntegrateShell { get; set; } = true;

    // --- Последнее состояние ---
    public string? LastFilePath { get; set; }

    // --- Недавнее ---
    public List<RecentEntry> RecentFiles { get; set; } = new();
}
