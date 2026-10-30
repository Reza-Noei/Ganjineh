using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Abstractions;
using PasswordManager.Desktop.Services;
using PasswordManager.Desktop.ViewModels;

namespace PasswordManager.Desktop;

public partial class MainWindow : Window
{
    private readonly AppViewModel _viewModel;
    private readonly IDialogService _dialogs;
    private readonly ClipboardService _clipboard;

    public MainWindow(AppViewModel viewModel, IDialogService dialogs, ClipboardService clipboard)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;
        _clipboard = clipboard;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Create.BrowseRequested += (_, _) =>
        {
            var folder = _dialogs.PickFolder(viewModel.Create.Location);
            if (folder is not null)
            {
                viewModel.Create.Location = folder;
            }
        };

        _clipboard.ClipboardExpired += (_, _) =>
        {
            var localizer = App.Services?.GetService(typeof(ILocalizer)) as ILocalizer;
            if (localizer is not null)
            {
                viewModel.Workspace.StatusMessage = localizer["Status.ClipboardCleared"];
            }
        };

        // Backup flows: the shell owns the Win32 file dialogs and hands paths into the VM.
        viewModel.Backup.CreateBackupRequested += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Ganjineh",
                Filter = "Ganjineh Backup (*.gvbak)|*.gvbak",
                FileName = $"ganjineh-backup-{DateTime.Now:yyyyMMdd-HHmm}.gvbak",
            };
            if (dialog.ShowDialog(this) == true)
            {
                await viewModel.Backup.CreateBackupToAsync(dialog.FileName);
            }
        };
        viewModel.Backup.VerifyBackupRequested += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ganjineh",
                Filter = "Ganjineh Backup (*.gvbak)|*.gvbak|All files|*.*",
            };
            if (dialog.ShowDialog(this) == true)
            {
                await viewModel.Backup.VerifyBackupAtAsync(dialog.FileName);
            }
        };
        viewModel.Backup.PickRestoreFileRequested += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ganjineh",
                Filter = "Ganjineh Backup (*.gvbak)|*.gvbak|All files|*.*",
            };
            if (dialog.ShowDialog(this) == true)
            {
                viewModel.Backup.SetRestoreFile(dialog.FileName);
            }
        };

        // Browser import wizard: the shell owns the Win32 dialogs and the post-import navigation.
        viewModel.BrowserImport.PickFileRequested += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ganjineh",
                Filter = "CSV (*.csv)|*.csv|All files|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) == true)
            {
                viewModel.BrowserImport.SetSelectedFile(dialog.FileName);
            }
        };
        viewModel.BrowserImport.OpenItemRequested += async (_, itemId) =>
        {
            await viewModel.Workspace.SelectItemByIdAsync(itemId);
        };
        viewModel.BrowserImport.OpenSourceFolderRequested += (_, sourcePath) =>
        {
            // Best-effort: highlight the source file so the user can delete it (§13). The app
            // itself never deletes or modifies the CSV.
            try
            {
                _ = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + sourcePath + "\""));
            }
            catch
            {
                // Explorer may be unavailable in restricted environments; nothing to recover.
            }
        };
    }

    /// <summary>Copies a PasswordBox's content into a char[] and clears the box.</summary>
    private static char[] TakeChars(System.Windows.Controls.PasswordBox box)
    {
        var chars = box.Password.ToCharArray();
        box.Clear();
        return chars;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : System.Windows.LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject start) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(start);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject start, string automationId) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(start);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is T match
                && string.Equals(match.GetValue(AutomationProperties.AutomationIdProperty), automationId))
            {
                return match;
            }

            var nested = FindDescendant<T>(child, automationId);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnUnlockPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is System.Windows.Controls.PasswordBox box)
        {
            _viewModel.Unlock.SetPassword(TakeChars(box));
            _viewModel.Unlock.UnlockCommand.Execute(null);
        }
    }

    private void OnCreateNextClick(object sender, RoutedEventArgs e)
    {
        var pane = FindAncestor<System.Windows.Controls.Grid>((DependencyObject)sender);
        var password = pane is null ? null : FindDescendant<System.Windows.Controls.PasswordBox>(pane, "CreatePassword");
        var confirm = pane is null ? null : FindDescendant<System.Windows.Controls.PasswordBox>(pane, "CreatePasswordConfirm");
        if (password is not null && confirm is not null)
        {
            _viewModel.Create.SetPasswords(TakeChars(password), TakeChars(confirm));
        }
    }

    private void OnCopyRecoveryKeyClick(object sender, RoutedEventArgs e)
    {
        if (App.Services?.GetService(typeof(ClipboardService)) is ClipboardService clipboard)
        {
            clipboard.TryCopyWithExpiry(_viewModel.Create.RecoveryKey);
        }
    }

    private void OnEditorSaveClick(object sender, RoutedEventArgs e)
    {
        // The editor PasswordBox lives in a DataTemplate name scope; find it relative to the button.
        var pane = FindAncestor<System.Windows.Controls.Border>((DependencyObject)sender);
        var passwordBox = pane is null
            ? FindDescendant<System.Windows.Controls.PasswordBox>((DependencyObject)Content)
            : FindDescendant<System.Windows.Controls.PasswordBox>(pane);
        if (passwordBox is null)
        {
            return;
        }

        _viewModel.Workspace.EditorPasswordHandoff = TakeChars(passwordBox);
        _viewModel.Workspace.SaveEditorCommand.Execute(null);
    }

    private async void OnAddAttachmentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Ganjineh" };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Workspace.AddAttachmentFromFileAsync(dialog.FileName);
        }
    }

    private async void OnExportAttachmentClick(object sender, RoutedEventArgs e)
    {
        var suggested = _viewModel.Workspace.SelectedAttachment?.Filename ?? "attachment";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Ganjineh",
            FileName = suggested.ReplaceInvalidFileNameChars(),
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Workspace.ExportAttachmentToFileAsync(dialog.FileName);
        }
    }

    /// <summary>Restore confirm: reads the master PasswordBox and hands it to the backup VM.
    /// (The RestoreRequest contract takes the credential as a string; the box is cleared here.)</summary>
    private async void OnBackupRestoreConfirmClick(object sender, RoutedEventArgs e)
    {
        var password = BackupRestorePassword.Password;
        BackupRestorePassword.Clear();
        await _viewModel.Backup.ConfirmRestoreAsync(password);
    }

    // ── keyboard: spec §7 (Ctrl+F search, Ctrl+N new, Ctrl+G generator, Ctrl+L lock), Esc closes
    //    the topmost overlay ──

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_viewModel.Screen != AppScreen.Workspace)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (TryCloseTopOverlay())
            {
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var workspace = _viewModel.Workspace;
        switch (e.Key)
        {
            case Key.F:
                workspace.FocusSearchCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N when NoOverlayOpen():
                workspace.BeginCreateCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.G when NoOverlayOpen():
                workspace.OpenGeneratorCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.L:
                workspace.LockCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private bool NoOverlayOpen() =>
        !_viewModel.Workspace.EditorVisible
        && !_viewModel.Workspace.GeneratorVisible
        && !_viewModel.Workspace.DeleteConfirmVisible
        && !_viewModel.Workspace.EmptyTrashConfirmVisible
        && !_viewModel.Settings.IsVisible
        && !_viewModel.Backup.IsVisible
        && !_viewModel.BrowserImport.IsVisible;

    private bool TryCloseTopOverlay()
    {
        var workspace = _viewModel.Workspace;
        if (_viewModel.BrowserImport.IsVisible)
        {
            // Close cancels any running import and drops the parsed session (Esc = safe exit).
            _viewModel.BrowserImport.CloseCommand.Execute(null);
            return true;
        }

        if (_viewModel.Settings.IsVisible)
        {
            _viewModel.Settings.IsVisible = false;
            return true;
        }

        if (_viewModel.Backup.IsVisible)
        {
            if (_viewModel.Backup.RestoreStage)
            {
                _viewModel.Backup.CancelRestoreStageCommand.Execute(null);
            }
            else
            {
                _viewModel.Backup.IsVisible = false;
            }

            return true;
        }

        if (workspace.EmptyTrashConfirmVisible)
        {
            workspace.CancelEmptyTrashCommand.Execute(null);
            return true;
        }

        if (workspace.DeleteConfirmVisible)
        {
            workspace.CancelDeleteCommand.Execute(null);
            return true;
        }

        if (workspace.GeneratorVisible)
        {
            workspace.CloseGeneratorCommand.Execute(null);
            return true;
        }

        if (workspace.EditorVisible)
        {
            workspace.CancelEditorCommand.Execute(null);
            return true;
        }

        return false;
    }
}

internal static class FileNameSanitizer
{
    public static string ReplaceInvalidFileNameChars(this string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "attachment" : cleaned;
    }
}
