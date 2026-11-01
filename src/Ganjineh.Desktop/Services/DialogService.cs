using Microsoft.Win32;
using Ganjineh.Application.Abstractions;

namespace Ganjineh.Desktop.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ganjineh",
            InitialDirectory = initialPath,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
