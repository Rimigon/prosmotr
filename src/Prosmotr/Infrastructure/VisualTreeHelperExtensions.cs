using System.Windows;
using System.Windows.Media;

namespace Prosmotr.Infrastructure;

/// <summary>Расширения для поиска элементов в визуальном дереве.</summary>
public static class VisualTreeHelperExtensions
{
    /// <summary>Находит первый визуальный потомок заданного типа.</summary>
    public static T? FindChild<T>(this DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>Находит все визуальные потомки заданного типа.</summary>
    public static IEnumerable<T> FindChildren<T>(this DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindChildren<T>(child))
                yield return descendant;
        }
    }
}
