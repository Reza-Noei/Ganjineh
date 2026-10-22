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
