using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Prosmotr.ViewModels;
using Prosmotr.Views;

namespace Prosmotr;

/// <summary>Точка входа: настройка DI-контейнера, создание главного окна, применение темы.</summary>
public partial class App : Application
{
    private IHost? _host;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private string _folderKey = "empty";
    private readonly CancellationTokenSource _appCts = new();
    private CancellationTokenSource _pipeCts = new();

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Splash screen: показываем мгновенно, пока грузятся сборки / DI / LibVLC.
        SplashScreen? splash = null;
        try
        {
            splash = new SplashScreen("Resources/Icons/app.ico");
            splash.Show(true); // auto-close при активации первого окна
        }
        catch { /* если иконка не подходит — работаем без splash */ }

        base.OnStartup(e);

        // Single-instance на уровне папки: одно окно на одну папку с медиафайлами.
        // Если для этой папки уже есть процесс — отправляем путь ему и выходим.
        var folderKey = GetFolderKey(ExtractPath(e.Args));
        _mutex = CreateOwnedMutex(MutexNameForKey(folderKey), out _ownsMutex);
        if (!_ownsMutex)
        {
            _mutex?.Dispose();
            _mutex = null;
            SendArgsToRunningInstance(folderKey, e.Args);
            Shutdown();
            return;
        }
        _folderKey = folderKey;
        StartPipeServerForFolder(folderKey);

        // Глобальная обработка непойманных исключений в UI-потоке.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Фоновые сбои (fire-and-forget задачи, continuations, STA-потоки) не доходят до
        // DispatcherUnhandledException — логируем их, иначе app.log пуст при таких ошибках.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("UnobservedTask", e.Exception);
            e.SetObserved(); // не валим процесс из-за непросмотренной задачи
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();
            _host.Start();

            var settings = Services.GetRequiredService<ISettingsService>();
            var theme = Services.GetRequiredService<IThemeService>();
            var window = Services.GetRequiredService<MainWindow>();

            // Очистка недавних при запуске, если включено в настройках.
            if (settings.Settings.ClearRecentOnStartup)
            {
                var recent = Services.GetRequiredService<IRecentFilesService>();
                recent.Clear();
            }

            MainWindow = window;
            window.Show(); // показываем первым, чтобы существовал HWND для Mica/слежения за темой
            splash?.Close(TimeSpan.FromSeconds(0)); // явно убираем splash (на всякий случай)

            theme.Initialize(window);
            theme.Apply(settings.Settings.Theme);

            var vm = Services.GetRequiredService<MainViewModel>();
            vm.FolderChanged += OnMainViewFolderChanged;

            // LibVLC warmup: генерируем plugins.dat если отсутствует, и создаём LibVLC в фоне.
            // Без кэша libvlc_new() сканирует всю папку plugins\ — 3–8 с задержки.
            var libvlcWarmup = Task.Run(() => LibVlcProvider.Warmup());

            var startupSw = Stopwatch.StartNew();
            // Ждём LibVLC в фоне перед открытием любого файла, иначе UI заблокируется
            // на время первого сканирования plugins\ при холодном старте.
            libvlcWarmup.ContinueWith(_ =>
            {
                // Окно закрыли во время прогрева (3-8 с на холодном старте) → хост уже выгружен.
                // Не дёргаем InitializeAsync на уничтоженном VM (как в OnSecondInstance/pipe-сервере).
                if (_appCts.IsCancellationRequested) return;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_appCts.IsCancellationRequested) return;
                        _ = vm.InitializeAsync(e.Args).ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                AppLog.Error("Startup InitializeAsync", t.Exception!);
                            else
                                AppLog.Write($"[Perf] Startup+open: {startupSw.ElapsedMilliseconds} ms");
                        }, TaskScheduler.Default);
                    });
                }
                catch (InvalidOperationException) { /* Dispatcher завершается при закрытии — игнорируем */ }
            }, TaskScheduler.Default);

            // Shell-интеграцию уводим в фон — реестр на холодной системе может подвисать.
            _ = Task.Run(TryIntegrateShell);
        }
        catch (Exception ex)
        {
            LogCrash("OnStartup", ex);
            MessageBox.Show("Не удалось запустить приложение:\n\n" + ex.Message,
                "Просмотр", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? ExtractPath(string[] args) =>
        args.FirstOrDefault(a => File.Exists(a) || Directory.Exists(a));

    private static void SendArgsToRunningInstance(string folderKey, string[] args)
    {
        var path = ExtractPath(args);
        try
        {
            using var client = new NamedPipeClientStream(".", PipeNameForKey(folderKey), PipeDirection.Out);
            // Первый процесс мог только что создать мьютекс и ещё не запустить pipe-сервер
            // (инициализация DI / LibVLC занимает время). Повторяем попытки подключения.
            for (int i = 0; i < 10; i++)
            {
                try { client.Connect(250); break; }
                catch (Exception) when (i < 9) { Thread.Sleep(100); }
            }
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path ?? string.Empty);
        }
        catch { /* не удалось — просто откроется новое окно в худшем случае */ }
    }

    private void StartPipeServerForFolder(string folderKey)
    {
        _pipeCts.Cancel();
        _pipeCts.Dispose();
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;
        var pipeName = PipeNameForKey(folderKey);

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    // Читаем ОГРАНИЧЕННЫЙ объём: путь Windows не длиннее ~32K символов.
                    // Без лимита локальный процесс мог бы слать гигабайты без '\n' и исчерпать
                    // память работающего экземпляра (DoS). Останавливаемся на первой строке.
                    const int maxBytes = 64 * 1024;
                    var buffer = new byte[maxBytes];
                    int total = 0, read;
                    while (total < maxBytes &&
                           (read = await server.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (Array.IndexOf(buffer, (byte)'\n', 0, total) >= 0) break;
                    }
                    var path = System.Text.Encoding.UTF8.GetString(buffer, 0, total).Split('\n', '\r')[0];
                    if (!_appCts.IsCancellationRequested)
                        Dispatcher.Invoke(() => OnSecondInstance(path));
                }
                catch (OperationCanceledException) { break; }
                catch { /* перезапускаем цикл ожидания */ }
            }
        });
    }

    private void OnSecondInstance(string path)
    {
        // Гонка с завершением: pipe мог сработать, когда хост уже выгружается —
        // обращение к Services тогда бросило бы ObjectDisposedException.
        if (_appCts.IsCancellationRequested) return;
        try
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            if (!string.IsNullOrEmpty(path))
                _ = vm.OpenPathAsync(path);

            if (MainWindow != null)
            {
                if (MainWindow.WindowState == WindowState.Minimized)
                    MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            }
        }
        catch (Exception ex)
        {
            LogCrash("SecondInstance", ex);
        }
    }

    private void OnMainViewFolderChanged(string folder)
    {
        try { BindToFolder(folder); }
        catch (Exception ex) { AppLog.Error("OnMainViewFolderChanged", ex); }
    }

    private void BindToFolder(string folder)
    {
        var newKey = GetFolderKey(folder);
        if (newKey == _folderKey) return;

        AppLog.Write($"[Instance] Switching bind from {_folderKey} to {newKey}");

        // Останавливаем старый pipe-сервер.
        try { _pipeCts.Cancel(); } catch { }
        try { _pipeCts.Dispose(); } catch { }
        _pipeCts = new CancellationTokenSource();

        // Освобождаем старый мьютекс.
        if (_ownsMutex && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _ownsMutex = false;
        }
        _mutex?.Dispose();
        _mutex = null;

        // Пытаемся захватить мьютекс новой папки.
        _mutex = CreateOwnedMutex(MutexNameForKey(newKey), out _ownsMutex);
        if (!_ownsMutex)
        {
            AppLog.Write($"[Instance] Could not own new folder mutex: {newKey}");
            _mutex?.Dispose();
            _mutex = null;
            return;
        }

        _folderKey = newKey;
        StartPipeServerForFolder(newKey);
    }

    internal static string GetFolderKey(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "empty";
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? string.Empty;
        if (string.IsNullOrEmpty(folder)) return "empty";
        return folder.ToLowerInvariant().TrimEnd('\\', '/');
    }

    private static string MutexNameForKey(string key)
    {
        var suffix = key == "empty" ? "empty" : HashKey(key);
        return "Prosmotr.SingleInstance." + suffix;
    }

    private static string PipeNameForKey(string key)
    {
        var suffix = key == "empty" ? "empty" : HashKey(key);
        return "Prosmotr.OpenFile." + suffix;
    }

    private static string HashKey(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private static Mutex? CreateOwnedMutex(string name, out bool owns)
    {
        owns = false;
        try
        {
            var m = new Mutex(true, name, out bool created);
            owns = created;
            return m;
        }
        catch (AbandonedMutexException)
        {
            try
            {
                var m = new Mutex(false, name, out _);
                try
                {
                    if (m.WaitOne(0))
                    {
                        owns = true;
                        return m;
                    }
                }
                catch (AbandonedMutexException)
                {
                    owns = true;
                    return m;
                }
                m.Dispose();
            }
            catch { }
        }
        catch { }
        return null;
    }

    private void TryIntegrateShell()
    {
        try
        {
            var settings = Services.GetRequiredService<ISettingsService>();
            var assoc = Services.GetRequiredService<IFileAssociationService>();
            // Регистрируем идемпотентно при каждом запуске, чтобы путь в реестре
            // всегда указывал на актуальный exe (debug или опубликованный).
            if (settings.Settings.IntegrateShell)
                assoc.Register();
        }
        catch (Exception ex)
        {
            LogCrash("ShellIntegration", ex);
        }
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prosmotr");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup.log"),
                $"[{DateTime.Now:O}] {source}: {ex}\n\n");
        }
        catch { /* логирование не должно падать */ }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Сервисы (singleton — общее состояние на всё приложение)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IMediaLibraryService, MediaLibraryService>();
        services.AddSingleton<IFileDeletionService, FileDeletionService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IRecentFilesService, RecentFilesService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IImageDecodingService, ImageDecodingService>();
        services.AddSingleton<IImageCache, ImageCache>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IPlaybackPositionStore, PlaybackPositionStore>();
        services.AddSingleton<IFileAssociationService, FileAssociationService>();
        services.AddSingleton<IDisplayTopologyService, DisplayTopologyService>();
        services.AddSingleton<LibVlcProvider>();
        services.AddSingleton<INotificationService, NotificationService>();

        // Фабрики для дочерних VM (P1.6)
        services.AddTransient<Func<MediaItem, ImageViewerViewModel>>(sp =>
            item => new ImageViewerViewModel(item, sp.GetRequiredService<IImageCache>(), sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<INotificationService>()));
        services.AddTransient<Func<MediaItem, VideoViewerViewModel>>(sp =>
            item => new VideoViewerViewModel(item, sp.GetRequiredService<LibVlcProvider>(), sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<IPlaybackPositionStore>(), sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<INotificationService>()));

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Окна (transient — контейнер может создавать новое, если потребуется)
        services.AddTransient(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<IServiceProvider>()));
        services.AddTransient<SettingsWindow>();
        services.AddTransient<FilePropertiesWindow>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("Dispatcher", e.Exception);
        MessageBox.Show(
            "Произошла непредвиденная ошибка:\n\n" + e.Exception.Message,
            "Просмотр", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // не даём приложению аварийно завершиться
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appCts.Cancel();
        // Восстанавливаем расширенный режим при выходе (если приложение включило clone).
        try
        {
            var topology = _host?.Services.GetService<IDisplayTopologyService>();
            if (topology != null)
            {
                AppLog.Write("App.OnExit: calling RestoreExtend");
                topology.RestoreExtend();
            }
        }
        catch (Exception ex) { AppLog.Error("App.OnExit", ex); }
        // Принудительно освобождаем текущий ViewModel (и плеер/Media) до выгрузки DI-хоста.
        try { (_host?.Services.GetService<MainViewModel>() as IDisposable)?.Dispose(); } catch { }
        // Гарантированно сбрасываем позиции видео на диск перед выгрузкой хоста.
        try { _host?.Services.GetService<IPlaybackPositionStore>()?.Flush(); } catch { }
        // Корректно освобождаем singletons (SettingsService, PlaybackPositionStore, LibVlcProvider).
        try { _host?.Dispose(); } catch { }
        try { _pipeCts.Cancel(); } catch { }
        try { _pipeCts.Dispose(); } catch { }
        // ReleaseMutex допустим только на владеющем экземпляре; на втором (не получившем
        // ownership) он бросил бы ApplicationException.
        if (_ownsMutex) { try { _mutex?.ReleaseMutex(); } catch { } }
        try { _mutex?.Dispose(); } catch { }
        try { _appCts.Dispose(); } catch { }
        base.OnExit(e);
    }
}
