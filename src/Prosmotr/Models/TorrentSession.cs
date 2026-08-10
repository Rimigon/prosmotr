using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prosmotr.Models;

/// <summary>
/// Наблюдаемая модель одной торрент-сессии. Создаётся движком сразу (статус
/// ResolvingMetadata) и обновляется фоном до ReadyToPlay/Error — UI подписывается
/// на PropertyChanged и не знает о MonoTorrent.
/// </summary>
public sealed partial class TorrentSession : ObservableObject
{
    [ObservableProperty]
    private TorrentStatus _status = TorrentStatus.ResolvingMetadata;

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private double _downloadedPercent;

    [ObservableProperty]
    private long _downloadSpeed;

    [ObservableProperty]
    private long _uploadSpeed;

    [ObservableProperty]
    private int _peersCount;

    [ObservableProperty]
    private long? _etaSeconds;

    /// <summary>Путь видеофайла на диске (куда движок пишет данные), для инфо и свойств.</summary>
    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>true — поток создан (первые+последние куски скачаны), можно звать Play().</summary>
    [ObservableProperty]
    private bool _isReadyToPlay;

    /// <summary>Seekable-поток из MonoTorrent StreamProvider. Не observable: отдаётся
    /// плееру напрямую через StreamMediaInput. Владение — у движка (закрывает в CloseSession).</summary>
    public Stream? Stream { get; set; }

    /// <summary>Общий размер выбранного файла в байтах (для ETA).</summary>
    public long TotalBytes { get; set; }

    /// <summary>InfoHash (нижний регистр) — для папки кэша и логов.</summary>
    public string? InfoHashHex { get; set; }

    /// <summary>Папка данных сессии (saveDirectory движка).</summary>
    public string? SaveDirectory { get; set; }

    /// <summary>Непрозрачный дескриптор движка (TorrentManager). Сессия/UI не должны
    /// знать тип — только движок приводит его обратно в CloseSessionAsync.</summary>
    public object? EngineRef { get; set; }
}
