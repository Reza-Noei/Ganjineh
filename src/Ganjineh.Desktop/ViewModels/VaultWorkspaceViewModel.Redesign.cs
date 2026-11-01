using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Desktop.Services;
using Ganjineh.Persistence.Items;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>
/// Redesign pass additions (2026-10-23 audit): live editor type switching, folder/tag nav
/// filtering (previously the nav selection never touched the filter), contextual list header,
/// empty-trash confirmation, search focus hook.
/// </summary>
public partial class VaultWorkspaceViewModel
{
    public bool EditorIsLogin => EditorKind == ItemType.Login;

    partial void OnEditorKindChanged(ItemType value)
    {
        OnPropertyChanged(nameof(EditorIsLogin));
        // Live type switching only while creating a new item; editing keeps the item's kind.
        if (EditorVisible && EditingId is null)
        {
            RebuildEditorDynamicFields();
        }
    }

    /// <summary>Re-renders the type-specific editor rows from the form registry, preserving the
    /// title/organization fields the user already typed.</summary>
    private void RebuildEditorDynamicFields()
    {
        EditorDataFields.Clear();
        foreach (var spec in ItemFormRegistry.Fields[EditorKind])
        {
            EditorDataFields.Add(new FieldEditorViewModel
            {
                DataKey = spec.DataKey, LabelKey = spec.LabelKey, Input = spec.Input,
            });
        }
    }

    [RelayCommand]
    private void SetEditorKind(ItemType kind)
    {
        if (EditorKind != kind)
        {
            EditorKind = kind;
        }
    }

    // ── nav filtering: folder/tag selection drives the item filter ──

    partial void OnSelectedFolderNavChanged(FolderNavRow? value)
    {
        FilterFolderId = value?.Id;
        OnPropertyChanged(nameof(ListHeader));
    }

    partial void OnSelectedTagNavChanged(TagNavRow? value)
    {
        FilterTagId = value?.Id;
        OnPropertyChanged(nameof(ListHeader));
    }

    partial void OnSectionChanged(string value)
    {
        // Section changes clear the organization filters so the nav highlight stays truthful
        // (nulling the nav selection cascades to FilterFolderId/FilterTagId via their partials).
        SelectedFolderNav = null;
        SelectedTagNav = null;
        TrashView = value == "Trash";
        OnPropertyChanged(nameof(ListHeader));
        OnPropertyChanged(nameof(SecuritySectionActive));
        _ = LoadAsync();
    }

    // Contextual title above the list: active folder/tag name, else the section label.
    public string ListHeader
    {
        get
        {
            if (SelectedFolderNav is { } folder)
            {
                return folder.Name;
            }

            if (SelectedTagNav is { } tag)
            {
                return tag.Name;
            }

            return Section switch
            {
                "Favorites" => _localizer["Nav.Favorites"],
                "Trash" => _localizer["Nav.Trash"],
                "Security" => _localizer["Nav.SecurityCenter"],
                { } s when s.StartsWith("Type:", StringComparison.Ordinal)
                           && Enum.TryParse<ItemType>(s[5..], out var kind)
                           && ItemFormRegistry.LabelKeys.TryGetValue(kind, out var key)
                    => _localizer[key],
                _ => _localizer["Center.ListHeader"],
            };
        }
    }

    // ── empty-trash confirmation (destructive, previously ran without asking) ──

    [ObservableProperty] private bool _emptyTrashConfirmVisible;

    [RelayCommand]
    private void BeginEmptyTrash()
    {
        if (TrashView && RowsView.Count > 0)
        {
            EmptyTrashConfirmVisible = true;
        }
    }

    [RelayCommand]
    private async Task ConfirmEmptyTrashAsync()
    {
        EmptyTrashConfirmVisible = false;
        var removed = await _items.EmptyTrashAsync(Session, 0);
        SetStatus(string.Format(_localizer["Status.TrashEmptied"], removed));
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEmptyTrash() => EmptyTrashConfirmVisible = false;

    // ── search focus hook (Ctrl+F) ──

    /// <summary>Raised so the shell can move keyboard focus into the search box.</summary>
    public event EventHandler? FocusSearchRequested;

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    // ── post-import navigation (Phase 9) ──

    /// <summary>Refreshes the list and selects the item written by the import wizard, so the
    /// user lands directly on the imported/updated credential.</summary>
    public async Task SelectItemByIdAsync(Guid id)
    {
        Section = "All";
        SearchText = string.Empty;
        await LoadAsync();
        var row = RowsView.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
            SelectedCredential = row;
        }
    }
}
