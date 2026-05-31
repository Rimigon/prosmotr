namespace Prosmotr.Services.Abstractions;

/// <summary>Удаление файла в Корзину или навсегда.</summary>
public interface IFileDeletionService
{
    /// <summary>Удалить файл. permanent=false — в Корзину. Возвращает true при успехе.</summary>
    Task<bool> DeleteAsync(string path, bool permanent);
}

/// <summary>Системные диалоги: открытие файла/папки и подтверждения.</summary>
public interface IDialogService
{
    string? OpenFile(IEnumerable<string> extensions);
    string? OpenFolder();
    Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText);
}

/// <summary>Взаимодействие с оболочкой Windows: проводник, буфер обмена, свойства файла.</summary>
public interface IShellService
{
    void ShowInExplorer(string path);
    void OpenContainingFolder(string path);
    void CopyPathToClipboard(string path);
    /// <summary>Открыть системный диалог «Открыть с помощью…».</summary>
    void OpenWith(string path);
    void OpenUri(string uri);
}
