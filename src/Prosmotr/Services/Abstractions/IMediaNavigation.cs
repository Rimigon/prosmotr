using Prosmotr.Models;

namespace Prosmotr.Services.Abstractions;

/// <summary>Результат построения галереи: список файлов и индекс открытого файла.</summary>
public sealed record MediaLibraryResult(IReadOnlyList<MediaItem> Items, int StartIndex);

/// <summary>Сбор поддерживаемых медиафайлов из папки в отсортированный список.</summary>
public interface IMediaLibraryService
{
    /// <summary>По открытому файлу собрать все медиафайлы его папки; StartIndex указывает на сам файл.
    /// Если задан explicitOrder (порядок из Проводника) — галерея упорядочивается по нему.</summary>
    Task<MediaLibraryResult> BuildFromFileAsync(string filePath, SortSpec sort,
        IReadOnlyList<string>? explicitOrder = null, CancellationToken ct = default);

    /// <summary>Собрать все медиафайлы папки.</summary>
    Task<MediaLibraryResult> BuildFromFolderAsync(string folderPath, SortSpec sort,
        IReadOnlyList<string>? explicitOrder = null, CancellationToken ct = default);

    /// <summary>Отсортировать список по заданным параметрам (вторичный ключ — натуральное имя).</summary>
    IReadOnlyList<MediaItem> Sort(IEnumerable<MediaItem> items, SortSpec sort);

    /// <summary>Создать одиночный элемент из пути (без сканирования папки).</summary>
    MediaItem? CreateItem(string path);
}

/// <summary>Хранит текущий список и позицию, реализует переходы prev/next и логику после удаления.</summary>
public interface INavigationService
{
    IReadOnlyList<MediaItem> Items { get; }
    int CurrentIndex { get; }
    MediaItem? Current { get; }
    bool HasItems { get; }
    bool CanGoNext { get; }
    bool CanGoPrevious { get; }

    /// <summary>Текущий элемент изменился (нужно перерисовать контент).</summary>
    event EventHandler? CurrentChanged;
    /// <summary>Список целиком изменился (например, после удаления или новой загрузки).</summary>
    event EventHandler? ListChanged;

    void SetItems(IReadOnlyList<MediaItem> items, int startIndex);
    void MoveNext();
    void MovePrevious();
    void MoveTo(int index);
    void MoveTo(MediaItem item);

    /// <summary>Удалить текущий из списка и перейти к следующему (или предыдущему, или в пустое состояние).</summary>
    void RemoveCurrent();

    /// <summary>Удалить элемент по конкретному индексу и перейти к следующему/предыдущему.
    /// Используется когда индекс был зафиксирован до асинхронной операции (удаление файла).</summary>
    void RemoveAt(int index);

    /// <summary>Вставить элемент в список по индексу и сделать его текущим (для отмены удаления).</summary>
    void InsertAt(MediaItem item, int index);

    /// <summary>Заменить порядок списка, сохранив текущий элемент (без перезагрузки контента).</summary>
    void ReorderPreservingCurrent(IReadOnlyList<MediaItem> items);

    void Clear();
}
