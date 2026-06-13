using System.IO;
using System.Reflection;
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

    // directory: переопределяемый каталог хранения (для тестов). DI использует значение
    // по умолчанию — %APPDATA%\Prosmotr (контейнер подставляет optional-параметр).
    public SettingsService(string? directory = null)
    {
        _dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prosmotr");
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
                if (loaded != null)
                {
                    ValidateAndFix(loaded);
                    return loaded;
                }
            }
        }
        catch { /* битый файл — используем значения по умолчанию */ }
        return new AppSettings();
    }

    // internal (не private) — чтобы покрыть юнит-тестами через InternalsVisibleTo.
    internal static void ValidateAndFix(AppSettings settings)
    {
        var defaults = new AppSettings();
        foreach (var prop in typeof(AppSettings).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            var value = prop.GetValue(settings);
            var attrs = prop.GetCustomAttributes<System.ComponentModel.DataAnnotations.ValidationAttribute>();
            foreach (var attr in attrs)
            {
                if (!attr.IsValid(value))
                {
                    prop.SetValue(settings, prop.GetValue(defaults));
                    break;
                }
            }
        }
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
                File.Move(tmp, _file, overwrite: true); // атомарно на одном томе, без TOCTOU-гонки
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
