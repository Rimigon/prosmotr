using System.IO;
using System.Text.Json;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Хранилище запомненных аудиодорожек для папок (сезонов сериалов) в
/// %LOCALAPPDATA%\Prosmotr\folder-audio-tracks.json. Отдельный файл от positions.json,
/// чтобы не ломать его формат (плоский Dictionary) и его тесты.</summary>
public sealed class FolderAudioTrackStore : IFolderAudioTrackStore, IDisposable
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, FolderAudioTrack> _map;
    private readonly System.Timers.Timer _debounce;

    // directory: переопределяемый каталог хранения (для тестов). DI использует значение
    // по умолчанию — %LOCALAPPDATA%\Prosmotr (контейнер подставляет optional-параметр).
    public FolderAudioTrackStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prosmotr");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "folder-audio-tracks.json");
        _map = Load();

        _debounce = new System.Timers.Timer(1500) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Flush();
    }

    private static string Key(string path) => path.ToLowerInvariant();

    public FolderAudioTrack? Get(string folderPath)
    {
        lock (_gate)
            return _map.TryGetValue(Key(folderPath), out var t) ? t : null;
    }

    public void Set(string folderPath, int audioTrackId, string? audioTrackName)
    {
        lock (_gate)
            _map[Key(folderPath)] = new FolderAudioTrack
            {
                AudioTrackId = audioTrackId,
                AudioTrackName = audioTrackName
            };
        ScheduleFlush();
    }

    public void Clear(string folderPath)
    {
        lock (_gate)
        {
            if (_map.Remove(Key(folderPath)))
                ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        if (!_debounce.Enabled) _debounce.Start();
    }

    public void Flush()
    {
        lock (_gate)
        {
            try
            {
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_map));
                File.Move(tmp, _file, overwrite: true); // атомарно на одном томе, без TOCTOU-гонки
            }
            catch { /* не критично */ }
        }
    }

    private Dictionary<string, FolderAudioTrack> Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, FolderAudioTrack>>(File.ReadAllText(_file));
                if (map != null) return map;
            }
        }
        catch { }
        return new Dictionary<string, FolderAudioTrack>();
    }

    public void Dispose()
    {
        _debounce.Dispose();
        Flush();
    }
}
