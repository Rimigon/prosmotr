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
    private readonly Dictionary<string, Task<ImageSource?>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();

    public ImageCache(IImageDecodingService decoder) => _decoder = decoder;

    public Task<ImageSource?> GetAsync(string path, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(path, out var existing))
            {
                // Не отдаём мёртвые задачи — иначе вызывающий получит OperationCanceledException
                // или AggregateException на старом токене, хотя новый токен ещё жив.
                if (existing.IsCompleted && (existing.IsCanceled || existing.IsFaulted))
                {
                    _map.Remove(path);
                    _lru.Remove(path);
                }
                else
                {
                    Touch(path);
                    return existing;
                }
            }

            var task = _decoder.LoadAsync(path, 0, ct);
            _map[path] = task;
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
            if (_map.TryGetValue(path, out var task) && task.IsCompletedSuccessfully && task.Result != null)
            {
                Touch(path);
                image = task.Result;
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
            _lru.RemoveLast();
            _map.Remove(oldest);
        }
    }
}
