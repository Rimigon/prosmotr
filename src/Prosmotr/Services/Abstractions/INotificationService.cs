namespace Prosmotr.Services.Abstractions;

/// <summary>Тип уведомления — задаёт цвет и иконку тоста.</summary>
public enum NotificationKind { Info, Success, Warning, Error }

/// <summary>Запрос на показ тоста. Action/ActionText — необязательная кнопка действия (напр. «Отменить»).</summary>
public sealed record NotificationRequest(
    string Message,
    NotificationKind Kind,
    string? ActionText,
    Action? Action,
    TimeSpan Duration);

/// <summary>
/// Показ ненавязчивых уведомлений (toast). Реализация лишь поднимает событие в UI-потоке;
/// рисуют тост сами View (см. <c>ToastView</c>) — это надёжнее WPF-UI Snackbar и работает
/// в т.ч. поверх оверлея видео.
/// </summary>
public interface INotificationService
{
    event EventHandler<NotificationRequest>? Requested;

    /// <summary>Простое уведомление без действия.</summary>
    void Show(string message, NotificationKind kind = NotificationKind.Info);

    /// <summary>Уведомление с кнопкой действия (живёт дольше, чтобы успеть нажать).</summary>
    void Show(string message, NotificationKind kind, string actionText, Action action);
}
