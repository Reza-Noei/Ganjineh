using PasswordManager.Application.Abstractions;

namespace PasswordManager.Application.Abstractions;

/// <summary>Folder/file dialogs, abstracted so view-models stay testable and WPF-free.</summary>
public interface IDialogService
{
    /// <summary>Returns the selected directory or null.</summary>
    string? PickFolder(string? initialPath = null);
}
