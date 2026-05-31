using System.Windows.Controls;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Prosmotr.Views;

/// <summary>
/// Общие пункты контекстного меню для медиа-вью (навигация и действия с файлом из MainViewModel).
/// Видео/фото-специфичные пункты (аудио, субтитры, скорость, поворот…) добавляют сами вью.
/// </summary>
internal static class MediaContextMenu
{
    /// <summary>Создать пункт меню с действием и (опц.) иконкой.</summary>
    public static MenuItem Item(string header, Action action, bool enabled = true, SymbolRegular? icon = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon.HasValue) item.Icon = new SymbolIcon { Symbol = icon.Value };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Пункт-переключатель (галочка) — для выбора дорожки/субтитров/скорости.</summary>
    public static MenuItem Check(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Переходы к предыдущему/следующему файлу.</summary>
    public static void AddNavigation(ItemCollection items, MainViewModel main)
    {
        items.Add(Item("Предыдущий", () => main.PreviousCommand.Execute(null),
            main.PreviousCommand.CanExecute(null), SymbolRegular.ChevronLeft24));
        items.Add(Item("Следующий", () => main.NextCommand.Execute(null),
            main.NextCommand.CanExecute(null), SymbolRegular.ChevronRight24));
    }

    /// <summary>Действия с файлом: проводник, копировать путь, открыть с помощью, свойства, удаление, восстановление.</summary>
    public static void AddFileActions(ItemCollection items, MainViewModel main)
    {
        items.Add(Item("Показать в проводнике", () => main.ShowInExplorerCommand.Execute(null),
            icon: SymbolRegular.FolderArrowRight24));
        items.Add(Item("Копировать путь", () => main.CopyPathCommand.Execute(null),
            icon: SymbolRegular.Copy24));
        items.Add(Item("Открыть с помощью…", () => main.OpenWithCommand.Execute(null),
            icon: SymbolRegular.Apps24));
        items.Add(Item("Свойства", () => main.ShowPropertiesCommand.Execute(null),
            icon: SymbolRegular.Info24));

        items.Add(new Separator());

        items.Add(Item("Удалить", () => main.DeleteCommand.Execute(null),
            main.DeleteCommand.CanExecute(null), SymbolRegular.Delete24));
        items.Add(Item("Восстановить последнее удаление", () => main.RestoreLastDeleteCommand.Execute(null),
            main.RestoreLastDeleteCommand.CanExecute(null), SymbolRegular.ArrowHookUpLeft24));
    }
}
