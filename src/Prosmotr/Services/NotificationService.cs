using System.Windows;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Поднимает <see cref="Requested"/> всегда в UI-потоке; отрисовку делает View (ToastView).</summary>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ActionDuration = TimeSpan.FromSeconds(6);

    public event EventHandler<NotificationRequest>? Requested;

    public void Show(string message, NotificationKind kind = NotificationKind.Info) =>
        Raise(new NotificationRequest(message, kind, null, null, DefaultDuration));

    public void Show(string message, NotificationKind kind, string actionText, Action action) =>
        Raise(new NotificationRequest(message, kind, actionText, action, ActionDuration));

    private void Raise(NotificationRequest request)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.CheckAccess())
            Requested?.Invoke(this, request);
        else
            app.Dispatcher.BeginInvoke(new Action(() => Requested?.Invoke(this, request)));
    }
}
