using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Лента миниатюр текущей папки; выбор миниатюры = переход к файлу.</summary>
public sealed partial class ThumbnailStripViewModel : ViewModelBase
{
    private const int ThumbSize = 128;
    private const int AddBatchSize = 100;

    private readonly IThumbnailService _thumbs;
    private readonly ConcurrentQueue<(ThumbnailEntry Entry, ImageSource Image)> _thumbQueue = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _thumbBatchTimer;
    private bool _suppressSelection;

    public ObservableCollection<ThumbnailEntry> Items { get; } = new();

    [ObservableProperty] private ThumbnailEntry? _selected;

    /// <summary>Пользователь выбрал миниатюру (нужно перейти к файлу).</summary>
    public event EventHandler<MediaItem>? SelectionRequested;

    public ThumbnailStripViewModel(IThumbnailService thumbs) => _thumbs = thumbs;

    partial void OnSelectedChanged(ThumbnailEntry? value)
    {
        if (value != null && !_suppressSelection)
            SelectionRequested?.Invoke(this, value.Item);
    }

    public async Task SetItemsAsync(IReadOnlyList<MediaItem> items)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Items.Clear();

        var entries = items.Select(it => new ThumbnailEntry(it)).ToList();

        // Добавляем порциями, чтобы UI-поток не блокировался на минуты
        for (int i = 0; i < entries.Count; i += AddBatchSize)
        {
            if (ct.IsCancellationRequested) return;

            int count = Math.Min(AddBatchSize, entries.Count - i);
            for (int j = 0; j < count; j++)
                Items.Add(entries[i + j]);

            // Даём WPF отрисовать кадр между пачками
            if (i + AddBatchSize < entries.Count)
                await Dispatcher.Yield(DispatcherPriority.Render);
        }

        _ = LoadThumbnailsAsync(entries, ct);
    }

    /// <summary>Подсветить миниатюру текущего файла без срабатывания навигации.</summary>
    public void SetCurrent(MediaItem? item)
    {
        _suppressSelection = true;
        try
        {
            foreach (var e in Items) e.IsSelected = false;

            var match = item == null
                ? null
                : Items.FirstOrDefault(e =>
                    string.Equals(e.Item.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));

            if (match != null) match.IsSelected = true;
            Selected = match;
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private async Task LoadThumbnailsAsync(List<ThumbnailEntry> entries, CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        await dispatcher.InvokeAsync(() =>
        {
            _thumbBatchTimer?.Stop();
            _thumbBatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _thumbBatchTimer.Tick += (_, _) => FlushThumbnailQueue();
            _thumbBatchTimer.Start();
        });

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(entries, options, async (entry, token) =>
        {
            try
            {
                var img = await _thumbs.GetThumbnailAsync(entry.Item, ThumbSize, token).ConfigureAwait(false);
                if (img == null || token.IsCancellationRequested) return;
                _thumbQueue.Enqueue((entry, img));
            }
            catch (OperationCanceledException) { }
            catch { /* игнорируем ошибки отдельных миниатюр */ }
        });

        await dispatcher.InvokeAsync(() =>
        {
            FlushThumbnailQueue();
            _thumbBatchTimer?.Stop();
            _thumbBatchTimer = null;
        });
    }

    private void FlushThumbnailQueue()
    {
        while (_thumbQueue.TryDequeue(out var pair))
        {
            if (pair.Entry.Thumbnail == null)
                pair.Entry.Thumbnail = pair.Image;
        }
    }
}
