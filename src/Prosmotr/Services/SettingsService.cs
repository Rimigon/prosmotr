using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Загрузка/сохранение настроек в %APPDATA%\Prosmotr\settings.json (атомарная запись).</summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dir;
    private readonly string _file;
    private readonly System.Timers.Timer _debounce;
    private readonly object _gate = new();

    public AppSettings Settings { get; private set; }
    public event EventHandler? SettingsChanged;

    public SettingsService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prosmotr");
        _file = Path.Combine(_dir, "settings.json");
        Settings = Load();

        _debounce = new System.Timers.Timer(800) { AutoReset = false };
        _debounce.Elapsed += (_, _) => WriteToDisk(); // фоновое сохранение без события
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null) return loaded;
            }
        }
        catch { /* битый файл — используем значения по умолчанию */ }
        return new AppSettings();
    }

    public void Save()
    {
        WriteToDisk();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveDebounced()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void WriteToDisk()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions));
                if (File.Exists(_file))
                    File.Replace(tmp, _file, null);
                else
                    File.Move(tmp, _file);
            }
            catch { /* сохранение настроек не критично для работы */ }
        }
    }

    public void Dispose()
    {
        _debounce.Dispose();
        WriteToDisk();
    }
}
