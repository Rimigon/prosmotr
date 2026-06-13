using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using Prosmotr.Infrastructure;

namespace Prosmotr.Services;

/// <summary>
/// Singleton-провайдер LibVLC. Поля static, чтобы фоновый Warmup и DI-singleton
/// использовали один и тот же нативный экземпляр LibVLC.
/// При первом доступе генерирует plugins.dat если он отсутствует
/// (это устраняет 3–8 с сканирования папки plugins\ при холодном старте).
/// В обычной работе использует --no-plugins-scan, полагаясь на кэш.
/// </summary>
public sealed class LibVlcProvider : IDisposable
{
    // static — единый экземпляр для всего процесса, инициализируется в Warmup или при первом доступе.
    private static readonly object _gate = new();
    private static LibVLC? _libVlc;
    private static bool _coreInitialized;
    private static bool _disposed;

    public LibVLC LibVlc
    {
        get
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LibVlcProvider));
                if (_libVlc == null)
                {
                    EnsureCoreInitialized();
                    // Кэш должен уже существовать (Warmup создаёт его раньше).
                    _libVlc = new LibVLC(
                        "--no-video-title-show",
                        "--quiet",
                        "--no-plugins-scan"); // не сканируем директории — только plugins.dat
                }
                return _libVlc;
            }
        }
    }

    private static void EnsureCoreInitialized()
    {
        // lock(_gate) переиспользуемо (re-entrant): getter и warmup-lock-ветка зовут отсюда
        // уже под _gate, а ранний вызов в начале Warmup — без внешнего lock.
        lock (_gate)
        {
            if (_coreInitialized) return;
            Core.Initialize();
            _coreInitialized = true;
        }
    }

    /// <summary>
    /// Генерирует plugins.dat в фоне при первом запуске и создаёт LibVLC заранее,
    /// чтобы UI-поток никогда не ждал сканирования plugins\.
    /// Статический — можно вызывать до резолва из DI.
    /// </summary>
    public static void Warmup()
    {
        // Core.Initialize() ОБЯЗАН отработать до ЛЮБОГО new LibVLC (настраивает путь к нативным
        // libvlc/plugins в libvlc\win-x64\). Идемпотентно — повторный вызов под lock ниже безвреден.
        EnsureCoreInitialized();

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins");
        var cacheFile = Path.Combine(pluginsDir, "plugins.dat");

        // Генерация кэша — долгая операция, выполняем БЕЗ lock.
        if (!File.Exists(cacheFile))
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var temp = new LibVLC("--reset-plugins-cache", "--quiet");
                AppLog.Write($"[Perf] Generated plugins.dat: {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                AppLog.Error("LibVlcProvider.Warmup generate cache", ex);
            }
        }

        // Быстрое создание LibVLC — под lock, чтобы не гоняться с getter/Dispose.
        lock (_gate)
        {
            if (_libVlc != null || _disposed) return;
            try
            {
                var sw = Stopwatch.StartNew();
                EnsureCoreInitialized();
                _libVlc = new LibVLC("--no-video-title-show", "--quiet", "--no-plugins-scan");
                AppLog.Write($"[Perf] LibVLC warmup: {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                AppLog.Error("LibVlcProvider.Warmup init", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _libVlc?.Dispose();
            _libVlc = null;
        }
    }
}
