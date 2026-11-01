using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Vaults;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>One row of the vault picker: only non-sensitive registry metadata.</summary>
public sealed record VaultRow(string VaultId, string DisplayName, string Path, DateTimeOffset? LastOpenedAt);

public partial class VaultPickerViewModel : ObservableObject
{
    private readonly IVaultCatalog _catalog;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;

    public VaultPickerViewModel(IVaultCatalog catalog, IDialogService dialogs, ILocalizer localizer)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        _localizer = localizer;
    }

    public ObservableCollection<VaultRow> Vaults { get; } = [];

    [ObservableProperty]
    private VaultRow? _selectedVault;

    public event EventHandler? CreateRequested;
    public event EventHandler<VaultRow>? UnlockRequested;

    [RelayCommand]
    public void Refresh()
    {
        Vaults.Clear();
        foreach (var vault in _catalog.List())
        {
            var displayName = vault.LocalLabel ?? System.IO.Path.GetFileName(vault.Path);
            Vaults.Add(new VaultRow(vault.VaultId, displayName, vault.Path, vault.LastOpenedAt));
        }

        SelectedVault ??= Vaults.FirstOrDefault();
    }

    [RelayCommand]
    private void Create() => CreateRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenExisting()
    {
        var folder = _dialogs.PickFolder();
        if (folder is null || !File.Exists(System.IO.Path.Combine(folder, "header.bin")))
        {
            return;
        }

        var dirName = System.IO.Path.GetFileName(folder);
        var vaultId = dirName.EndsWith(".pvault", StringComparison.Ordinal)
            ? dirName[..^7]
            : dirName;
        var row = new VaultRow(vaultId, dirName, folder, null);
        UnlockRequested?.Invoke(this, row);
    }

    [RelayCommand]
    private void RemoveFromList()
    {
        if (SelectedVault is { } row)
        {
            _catalog.RemoveFromList(row.VaultId);
            Refresh();
        }
    }

    [RelayCommand]
    private void DeleteVault()
    {
        if (SelectedVault is { } row)
        {
            // The view shows an explicit confirmation overlay before calling this.
            _catalog.DeleteVault(row.VaultId);
            Refresh();
        }
    }

    [RelayCommand]
    private void Unlock() => UnlockRequested?.Invoke(this, SelectedVault!);
}
