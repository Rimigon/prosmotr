using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Prosmotr.ViewModels;
using Prosmotr.Views;

namespace Prosmotr;

/// <summary>Точка входа: настройка DI-контейнера, создание главного окна, применение темы.</summary>
public partial class App : Application
{
    private const string MutexName = "Prosmotr.SingleInstance.v1";
    private const string PipeName = "Prosmotr.OpenFile.v1";

    private IHost? _host;
    private Mutex? _mutex;
    private readonly CancellationTokenSource _appCts = new();

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance: если приложение уже запущено — передаём путь ему и выходим.
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            SendArgsToRunningInstance(e.Args);
            Shutdown();
            return;
        }
        StartPipeServer();

        // Глобальная обработка непойманных исключений в UI-потоке.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

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

            theme.Initialize(window);
            theme.Apply(settings.Settings.Theme);

            var vm = Services.GetRequiredService<MainViewModel>();
            _ = vm.InitializeAsync(e.Args);

            TryIntegrateShell();
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

    private static void SendArgsToRunningInstance(string[] args)
    {
        var path = ExtractPath(args);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path);
        }
        catch { /* не удалось — просто откроется новое окно в худшем случае */ }
    }

    private void StartPipeServer()
    {
        Task.Run(async () =>
        {
            while (!_appCts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_appCts.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var path = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(path) && !_appCts.IsCancellationRequested)
                        Dispatcher.Invoke(() => OnSecondInstance(path));
                }
                catch (OperationCanceledException) { break; }
                catch { /* перезапускаем цикл ожидания */ }
            }
        });
    }

    private void OnSecondInstance(string path)
    {
        try
        {
            var vm = Services.GetRequiredService<MainViewModel>();
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

        // Окна
        services.AddSingleton<MainWindow>();
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
        // Гарантированно сбрасываем позиции видео на диск перед выгрузкой хоста.
        try { _host?.Services.GetService<IPlaybackPositionStore>()?.Flush(); } catch { }
        // Корректно освобождаем singletons (SettingsService, PlaybackPositionStore, LibVlcProvider).
        try { _host?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        try { _appCts.Dispose(); } catch { }
        base.OnExit(e);
    }
}
