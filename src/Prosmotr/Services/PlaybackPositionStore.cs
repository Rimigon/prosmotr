using System.IO;
using System.Text.Json;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Хранилище позиций воспроизведения видео (resume) в %LOCALAPPDATA%\Prosmotr\positions.json.</summary>
public sealed class PlaybackPositionStore : IPlaybackPositionStore, IDisposable
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, PlaybackPosition> _map;
    private readonly System.Timers.Timer _debounce;

    public PlaybackPositionStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prosmotr");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "positions.json");
        _map = Load();

        _debounce = new System.Timers.Timer(1500) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Flush();
    }

    private static string Key(string path) => path.ToLowerInvariant();

    public PlaybackPosition? Get(string path)
    {
        lock (_gate)
            return _map.TryGetValue(Key(path), out var p) ? p : null;
    }

    public void Save(string path, long positionMs, long durationMs, float? rate)
    {
        lock (_gate)
            _map[Key(path)] = new PlaybackPosition { PositionMs = positionMs, DurationMs = durationMs, Rate = rate };
        ScheduleFlush();
    }

    public void Remove(string path)
    {
        lock (_gate)
            _map.Remove(Key(path));
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        // Не перезапускаем таймер на каждое сохранение, иначе при непрерывном
        // воспроизведении (сохранение раз в секунду) флаш бы «голодал».
        // Так получаем периодический сброс на диск примерно раз в 1.5 с.
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
                if (File.Exists(_file))
                    File.Replace(tmp, _file, null);
                else
                    File.Move(tmp, _file);
            }
            catch { /* не критично */ }
        }
    }

    private Dictionary<string, PlaybackPosition> Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, PlaybackPosition>>(File.ReadAllText(_file));
                if (map != null) return map;
            }
        }
        catch { }
        return new Dictionary<string, PlaybackPosition>();
    }

    public void Dispose()
    {
        _debounce.Dispose();
        Flush();
    }
}
