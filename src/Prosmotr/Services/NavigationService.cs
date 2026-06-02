using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Хранит галерею и позицию, реализует циклическую навигацию и поведение после удаления.</summary>
public sealed class NavigationService : INavigationService
{
    private List<MediaItem> _items = new();
    private int _index = -1;

    public IReadOnlyList<MediaItem> Items => _items;
    public int CurrentIndex => _index;
    public MediaItem? Current => _index >= 0 && _index < _items.Count ? _items[_index] : null;
    public bool HasItems => _items.Count > 0;

    // Циклическая навигация: стрелки работают всегда, когда файлов больше одного.
    public bool CanGoNext => _items.Count > 1;
    public bool CanGoPrevious => _items.Count > 1;

    public event EventHandler? CurrentChanged;
    public event EventHandler? ListChanged;

    public void SetItems(IReadOnlyList<MediaItem> items, int startIndex)
    {
        _items = items.ToList();
        _index = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        ListChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveNext()
    {
        if (_items.Count == 0) return;
        _index = (_index + 1) % _items.Count;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePrevious()
    {
        if (_items.Count == 0) return;
        _index = (_index - 1 + _items.Count) % _items.Count;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count || index == _index) return;
        _index = index;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveTo(MediaItem item)
    {
        var idx = _items.FindIndex(i => ReferenceEquals(i, item) ||
            string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));
        MoveTo(idx);
    }

    public void RemoveCurrent()
    {
        if (_index < 0 || _index >= _items.Count) return;
        RemoveAt(_index);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        _items.RemoveAt(index);

        if (_items.Count == 0)
        {
            _index = -1; // пустое состояние
        }
        else if (index == _index)
        {
            // Удалён именно текущий — индекс уже указывает на «следующий»
            // (список сдвинулся влево). Если удаляли последний — показываем предыдущий.
            if (_index >= _items.Count)
                _index = _items.Count - 1;
        }
        else if (index < _index)
        {
            // Удалён элемент перед текущим — текущий сдвинулся влево.
            _index--;
        }
        // иначе удалён после текущего — ничего не меняется

        ListChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InsertAt(MediaItem item, int index)
    {
        if (_items.Any(i => string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            MoveTo(item); // уже в списке — просто перейдём к нему
            return;
        }

        var at = Math.Clamp(index, 0, _items.Count);
        _items.Insert(at, item);
        _index = at;
        ListChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReorderPreservingCurrent(IReadOnlyList<MediaItem> items)
    {
        var current = Current;
        _items = items.ToList();

        // Находим текущий файл в новом порядке, чтобы prev/next и подсветка остались на нём.
        if (current != null)
        {
            var idx = _items.FindIndex(i =>
                string.Equals(i.FullPath, current.FullPath, StringComparison.OrdinalIgnoreCase));
            _index = idx >= 0 ? idx : (_items.Count == 0 ? -1 : 0);
        }
        else
        {
            _index = _items.Count == 0 ? -1 : 0;
        }

        // Только ListChanged: контент (фото/видео) не перезагружаем, меняется лишь порядок.
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _items = new List<MediaItem>();
        _index = -1;
        ListChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
