using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Кэш полноразмерных изображений (LRU) для мгновенного переключения между соседними фото.</summary>
public interface IImageCache
{
    Task<ImageSource?> GetAsync(string path, CancellationToken ct = default);
    void Preload(IEnumerable<string> paths);

    /// <summary>Вернуть уже готовое (декодированное) изображение синхронно — для переключения без мигания.</summary>
    bool TryGetLoaded(string path, out ImageSource? image);
}

/// <summary>Небольшой LRU-кэш декодированных изображений поверх IImageDecodingService.</summary>
public sealed class ImageCache : IImageCache
{
    private const int Capacity = 24;

    private readonly IImageDecodingService _decoder;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();

    public ImageCache(IImageDecodingService decoder) => _decoder = decoder;

    public Task<ImageSource?> GetAsync(string path, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(path, out var existing))
            {
                if (existing.Task.IsCompleted && (existing.Task.IsCanceled || existing.Task.IsFaulted))
                {
                    RemoveEntry(path);
                }
                else
                {
                    Touch(path);
                    return existing.Task;
                }
            }

            var cts = new CancellationTokenSource();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            var task = _decoder.LoadAsync(path, 0, linked.Token);

            // После завершения задачи освобождаем связанный CTS.
            _ = task.ContinueWith(_ => linked.Dispose(), TaskScheduler.Default);

            _map[path] = new CacheEntry { Task = task, Cts = cts };
            _lru.AddFirst(path);
            Trim();
            return task;
        }
    }

    public bool TryGetLoaded(string path, out ImageSource? image)
    {
        image = null;
        lock (_gate)
        {
            if (_map.TryGetValue(path, out var entry) && entry.Task.IsCompletedSuccessfully && entry.Task.Result != null)
            {
                Touch(path);
                image = entry.Task.Result;
                return true;
            }
        }
        return false;
    }

    public void Preload(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            // запускаем загрузку в фоне и складываем в кэш; ошибки игнорируются
            _ = GetAsync(p);
        }
    }

    private void Touch(string path)
    {
        _lru.Remove(path);
        _lru.AddFirst(path);
    }

    private void Trim()
    {
        while (_lru.Count > Capacity)
        {
            var oldest = _lru.Last!.Value;
            RemoveEntry(oldest);
        }
    }

    private void RemoveEntry(string path)
    {
        _lru.Remove(path);
        if (_map.Remove(path, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
        }
    }

    private sealed class CacheEntry
    {
        public required Task<ImageSource?> Task;
        public required CancellationTokenSource Cts;
    }
}
