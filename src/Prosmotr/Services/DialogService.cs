using Microsoft.Win32;
using Prosmotr.Services.Abstractions;
using Wpf.Ui.Controls;

namespace Prosmotr.Services;

/// <summary>Системные диалоги открытия файла/папки и подтверждения (через WPF-UI MessageBox).</summary>
public sealed class DialogService : IDialogService
{
    public string? OpenFile(IEnumerable<string> extensions)
    {
        var masks = extensions.Select(e => "*" + e).ToArray();
        var filter = $"Медиафайлы ({string.Join(", ", masks)})|{string.Join(";", masks)}|Все файлы (*.*)|*.*";
        var dlg = new OpenFileDialog
        {
            Title = "Открыть фото или видео",
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? OpenFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Открыть папку",
            Multiselect = false
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            PrimaryButtonAppearance = ControlAppearance.Danger
        };

        var result = await box.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}
