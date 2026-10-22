using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Application.Abstractions;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Items;
using PasswordManager.Application.Vaults;
using PasswordManager.Desktop.Services;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.VaultHeader;

namespace PasswordManager.Desktop.ViewModels;

/// <summary>Row for the center list: never carries password material.</summary>
public sealed class CredentialRowViewModel
{
    public Guid Id { get; init; }
    public ItemType Kind { get; init; } = ItemType.Login;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public bool Favorite { get; set; }

    /// <summary>Phase 3 compatibility alias.</summary>
    public string? Username => Subtitle;
    public string? WebsiteUrl => null;
}

/// <summary>Dynamic editor row (type-specific field), metadata-driven.</summary>
public partial class FieldEditorViewModel : ObservableObject
{
    public string DataKey { get; init; } = string.Empty;
    public string LabelKey { get; init; } = string.Empty;
    public InputKind Input { get; init; }

    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _booleanValue;

    public bool IsMultiline => Input is InputKind.Multiline;
    public bool IsBoolean => Input is InputKind.Boolean;
    public bool IsSingleLine => !IsMultiline;

    partial void OnBooleanValueChanged(bool value) => Value = value ? "true" : "false";
}

/// <summary>Custom-field editor row.</summary>
public partial class CustomFieldEditorViewModel : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private int _typeIndex;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _concealed;

    public static readonly CustomFieldType[] CustomFieldTypeValues =
    [
        CustomFieldType.Text, CustomFieldType.Concealed, CustomFieldType.Multiline,
        CustomFieldType.Url, CustomFieldType.Email, CustomFieldType.Number,
        CustomFieldType.Date, CustomFieldType.Boolean,
    ];

    public CustomField ToField(int order) => new(
        Id.ToString(), Label.Trim(), CustomFieldTypeValues[TypeIndex],
        string.IsNullOrWhiteSpace(Value) ? null : Value, order, Concealed);
}

public sealed record FolderNavRow(Guid Id, string Name, Guid? ParentId, int Count);
public sealed record TagNavRow(Guid Id, string Name, int Count);
public sealed record AttachmentRowVm(Guid Id, string Filename, long SizeBytes, DateTimeOffset CreatedAt);
public sealed record VersionRowVm(int Version, DateTimeOffset CreatedAt);

/// <summary>One rendered details row: label + value; masked when the field is secret-class.</summary>
public sealed record FieldRowVm(string Label, string Value, bool Masked)
{
    public string Display => Masked ? "••••••••" : Value;
}

/// <summary>
/// The vault workspace: unified items (all kinds), organization, history, trash, attachments —
/// bound to the single active session and wiped on every session boundary.
/// </summary>
public sealed partial class VaultWorkspaceViewModel : ObservableObject
{
    private readonly IVaultLifecycle _lifecycle;
    private readonly IVaultSessionManager _sessions;
    private readonly IVaultItemService _items;
    private readonly IFolderService _folders;
    private readonly ITagService _tags;
    private readonly IAttachmentService _attachments;
    private readonly ClipboardService _clipboard;
    private readonly ILocalizer _localizer;

    public VaultWorkspaceViewModel(
        IVaultLifecycle lifecycle,
        IVaultSessionManager sessions,
        IVaultItemService items,
        IFolderService folders,
        ITagService tags,
        IAttachmentService attachments,
        ClipboardService clipboard,
        ILocalizer localizer)
    {
        _lifecycle = lifecycle;
        _sessions = sessions;
        _items = items;
        _folders = folders;
        _tags = tags;
        _attachments = attachments;
        _clipboard = clipboard;
        _localizer = localizer;
        _sessions.SessionChanged += OnSessionChanged;
    }

    public VaultSession Session => _sessions.Current ?? throw new InvalidOperationException("No active vault session.");

    [ObservableProperty] private string _vaultDisplayName = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private CredentialRowViewModel? _selectedCredential;
    [ObservableProperty] private ItemDetail? _detail;
    [ObservableProperty] private bool _passwordRevealed;
    [ObservableProperty] private bool _editorVisible;
    [ObservableProperty] private bool _deleteConfirmVisible;
    [ObservableProperty] private bool _trashView;
    [ObservableProperty] private string _section = "All";
    [ObservableProperty] private Guid? _filterFolderId;
    [ObservableProperty] private Guid? _filterTagId;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private string _sortBy = "updated";
    [ObservableProperty] private string _newFolderName = string.Empty;
    [ObservableProperty] private string _newTagName = string.Empty;
    [ObservableProperty] private FolderNavRow? _selectedFolderNav;
    [ObservableProperty] private TagNavRow? _selectedTagNav;
    [ObservableProperty] private AttachmentRowVm? _selectedAttachment;
    [ObservableProperty] private VersionRowVm? _selectedVersion;

    // Editor state
    [ObservableProperty] private ItemType _editorKind = ItemType.Login;
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _editorUsername = string.Empty;
    [ObservableProperty] private string _editorWebsiteUrl = string.Empty;
    [ObservableProperty] private string _editorNotes = string.Empty;
    [ObservableProperty] private string _editorDescription = string.Empty;
    [ObservableProperty] private Guid? _editorFolderId;
    [ObservableProperty] private string _editorTagNames = string.Empty;
    [ObservableProperty] private string? _editorError;
    public char[] EditorPasswordHandoff { get; set; } = [];
    public Guid? EditingId { get; private set; }

    public ObservableCollection<FieldEditorViewModel> EditorDataFields { get; } = [];
    public ObservableCollection<CustomFieldEditorViewModel> EditorCustomFields { get; } = [];
    public IReadOnlyList<ItemType> EditorKindChoices { get; } =
    [
        ItemType.Login, ItemType.SecureNote, ItemType.PaymentCard, ItemType.Identity,
        ItemType.Wifi, ItemType.ApiCredential, ItemType.SoftwareLicense,
        ItemType.RecoveryCodes, ItemType.Custom,
    ];

    public ObservableCollection<CredentialRowViewModel> RowsView { get; } = [];
    public ObservableCollection<FolderNavRow> FolderNav { get; } = [];
    public ObservableCollection<TagNavRow> TagNav { get; } = [];
    public ObservableCollection<AttachmentRowVm> Attachments { get; } = [];
    public ObservableCollection<VersionRowVm> Versions { get; } = [];

    private List<ItemSummary> _allCredentials = [];
    private Dictionary<Guid, string> _searchIndex = new();
    private Dictionary<Guid, string> _folderNames = new();
    private Dictionary<Guid, string> _tagNames = new();
    private CancellationTokenSource? _searchDebounce;
    private Guid? _pendingPurgeId;

    public bool HasNoResults => RowsView.Count == 0 && !IsBusy && !HasError;

    public string PasswordDisplay => PasswordRevealed ? LoginPassword() ?? string.Empty : "••••••••";

    private string? LoginPassword() =>
        Detail is not null && Detail.Kind == ItemType.Login
            ? Detail.Data.GetValueOrDefault("password") as string
            : null;

    public event EventHandler? LockRequested;
    public event EventHandler? EditorPasswordCleared;
    public event EventHandler? AddAttachmentRequested;
    public event EventHandler<(Guid AttachmentId, string SuggestedName)>? ExportAttachmentRequested;

    // ── derived display ──

    public string DetailTitleText => Detail?.Title ?? string.Empty;
    public string DetailUsernameText =>
        Detail?.Kind == ItemType.Login ? Detail.Data.GetValueOrDefault("username") as string ?? string.Empty : ItemKindInfo.SubtitleFor(Detail!.Kind, Detail.Data) ?? string.Empty;
    public string DetailWebsiteText =>
        Detail?.Kind == ItemType.Login ? FirstUrl(Detail) ?? string.Empty : string.Empty;
    public string DetailNotesText =>
        Detail?.Kind == ItemType.Login ? Detail.Description ?? string.Empty : Detail?.Description ?? string.Empty;
    public string DetailDates =>
        Detail is null ? string.Empty : $"{_localizer["Details.Created"]} {Detail.CreatedAt.LocalDateTime:yyyy/MM/dd HH:mm}   |   {_localizer["Details.Modified"]} {Detail.UpdatedAt.LocalDateTime:yyyy/MM/dd HH:mm}";

    private static string? FirstUrl(ItemDetail detail) =>
        detail.Data.TryGetValue("urls", out var value) && value is System.Text.Json.JsonElement el
        && el.ValueKind == System.Text.Json.JsonValueKind.Array
        && el.EnumerateArray().FirstOrDefault().ValueKind == System.Text.Json.JsonValueKind.String
            ? el.EnumerateArray().First().GetString()
            : null;

    partial void OnPasswordRevealedChanged(bool value) => OnPropertyChanged(nameof(PasswordDisplay));

    partial void OnSectionChanged(string value)
    {
        TrashView = value == "Trash";
        _ = LoadAsync();
    }

    partial void OnFilterFolderIdChanged(Guid? value) => ApplyFilter();
    partial void OnFilterTagIdChanged(Guid? value) => ApplyFilter();
    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilter();
    partial void OnSortByChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(ApplyFilter);
        }, token);
    }

    partial void OnSelectedCredentialChanged(CredentialRowViewModel? value)
    {
        if (value is null)
        {
            Detail = null;
            return;
        }

        _ = LoadDetailAsync(value.Id);
    }

    public bool IsLoginDetail => Detail?.Kind == ItemType.Login;
    public bool IsGenericDetail => Detail is { Kind: not ItemType.Login };

    partial void OnDetailChanged(ItemDetail? value)
    {
        OnPropertyChanged(nameof(IsLoginDetail));
        OnPropertyChanged(nameof(IsGenericDetail));
    }

    [RelayCommand]
    private void SetSection(string section) => Section = section;

    // ── loading ──

    public async Task LoadAsync()
    {
        if (!_sessions.HasActiveSession)
        {
            return;
        }

        var session = Session;
        VaultDisplayName = _lifecycle.GetVaultName(session) ?? session.VaultIdText;
        IsBusy = true;
        HasError = false;
        try
        {
            var summaries = await _items.ListAsync(session, BuildFilter());
            _allCredentials = summaries.ToList();
            var index = await _items.BuildSearchIndexAsync(session, BuildFilter());
            _searchIndex = index.ToDictionary(e => e.Id, e => e.SearchText);
            RebuildRows(_allCredentials);
            ApplyFilter();
            await LoadOrganizationAsync();
        }
        catch (StaleSessionException)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadOrganizationAsync()
    {
        var folders = await _folders.ListAsync(Session);
        var folderCounts = await _folders.GetCountsAsync(Session);
        _folderNames = folders.ToDictionary(f => f.Id, f => f.Name);
        FolderNav.Clear();
        foreach (var folder in folders)
        {
            FolderNav.Add(new FolderNavRow(folder.Id, folder.Name, folder.ParentId, folderCounts.GetValueOrDefault(folder.Id)));
        }

        var tags = await _tags.ListAsync(Session);
        _tagNames = tags.ToDictionary(t => t.Id, t => t.Name);
        TagNav.Clear();
        foreach (var tag in tags)
        {
            TagNav.Add(new TagNavRow(tag.Id, tag.Name, tag.UsageCount));
        }
    }

    private ItemFilter BuildFilter()
    {
        ItemType? kind = Section.StartsWith("Type:", StringComparison.Ordinal)
            && Enum.TryParse<ItemType>(Section[5..], out var parsed) ? parsed : null;
        var favorites = FavoritesOnly || Section == "Favorites";
        return new ItemFilter(
            Kind: kind,
            FolderId: FilterFolderId,
            TagIds: FilterTagId is { } tag ? [tag] : null,
            FavoritesOnly: favorites,
            TrashedOnly: Section == "Trash",
            SortBy: SortBy);
    }

    private void RebuildRows(IEnumerable<ItemSummary> summaries)
    {
        CredentialsClear();
        foreach (var s in summaries)
        {
            RowsView.Add(new CredentialRowViewModel
            {
                Id = s.Id, Kind = s.Kind, Title = s.Title, Subtitle = s.Subtitle, Favorite = s.Favorite,
            });
        }
    }

    private void CredentialsClear() => RowsView.Clear();

    private void ApplyFilter()
    {
        var query = SearchNormalizer.Normalize(SearchText);
        var previousId = SelectedCredential?.Id;
        var filtered = _allCredentials
            .Where(s => query.Length == 0
                        || (_searchIndex.TryGetValue(s.Id, out var text)
                            && SearchNormalizer.Matches(text, query))
                        || SearchNormalizer.Matches(s.Title, query)
                        || SearchNormalizer.Matches(s.Subtitle, query))
            .Select(s => new CredentialRowViewModel
            {
                Id = s.Id, Kind = s.Kind, Title = s.Title, Subtitle = s.Subtitle, Favorite = s.Favorite,
            });

        RowsView.Clear();
        foreach (var row in filtered)
        {
            RowsView.Add(row);
        }

        // Keep the selection stable across rebuilds when the item is still visible.
        SelectedCredential = previousId is null ? null : RowsView.FirstOrDefault(r => r.Id == previousId);
        OnPropertyChanged(nameof(HasNoResults));
    }

    private async Task LoadDetailAsync(Guid id)
    {
        try
        {
            PasswordRevealed = false;
            Detail = await _items.GetAsync(Session, id);
            OnPropertyChanged(nameof(DetailTitleText));
            OnPropertyChanged(nameof(DetailUsernameText));
            OnPropertyChanged(nameof(DetailWebsiteText));
            OnPropertyChanged(nameof(DetailNotesText));
            OnPropertyChanged(nameof(DetailDates));
            RebuildDetailRows();
            Attachments.Clear();
            foreach (var attachment in await _attachments.ListAsync(Session, id))
            {
                Attachments.Add(new AttachmentRowVm(attachment.Id, attachment.Filename, attachment.SizeBytes, attachment.CreatedAt));
            }

            Versions.Clear();
            foreach (var version in await _items.GetHistoryAsync(Session, id))
            {
                Versions.Add(new VersionRowVm(version.Version, version.CreatedAt));
            }
        }
        catch (StaleSessionException)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or Persistence.DatabaseCorruptionException)
        {
            HasError = true;
        }
    }

    private void RebuildDetailRows()
    {
        if (Detail is null)
        {
            DetailDataRows = [];
            DetailCustomRows = [];
            return;
        }

        var rows = new List<FieldRowVm>();
        foreach (var spec in ItemFormRegistry.Fields[Detail.Kind])
        {
            if (!Detail.Data.TryGetValue(spec.DataKey, out var value))
            {
                continue;
            }

            var text = value switch
            {
                System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.Array =>
                    string.Join("  |  ", el.EnumerateArray().Select(e => e.GetString() ?? string.Empty)),
                System.Text.Json.JsonElement el => el.ToString(),
                _ => value?.ToString() ?? string.Empty,
            };
            if (text.Length == 0)
            {
                continue;
            }

            var masked = spec.Input is InputKind.Password;
            rows.Add(new FieldRowVm(_localizer[spec.LabelKey], text, masked));
        }

        DetailDataRows = rows;
        DetailCustomRows = Detail.CustomFields
            .OrderBy(f => f.Order)
            .Select(f => new FieldRowVm(f.Label, f.Value ?? string.Empty, f.Concealed))
            .ToList();
    }

    [ObservableProperty] private IReadOnlyList<FieldRowVm> _detailDataRows = [];
    [ObservableProperty] private IReadOnlyList<FieldRowVm> _detailCustomRows = [];
    [ObservableProperty] private ItemType _detailKind = ItemType.Login;
    [ObservableProperty] private IReadOnlyList<string> _detailTagNames = [];
    [ObservableProperty] private Guid? _detailFolderId;

    // ── details actions ──

    [RelayCommand]
    private void ToggleReveal() => PasswordRevealed = !PasswordRevealed;

    [RelayCommand]
    private void CopyUsername()
    {
        if (DetailUsernameText.Length > 0)
        {
            SetStatus(_clipboard.TryCopyWithExpiry(DetailUsernameText)
                ? _localizer["Status.CopiedUsername"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    [RelayCommand]
    private void CopyPassword()
    {
        var password = LoginPassword();
        if (!string.IsNullOrEmpty(password))
        {
            SetStatus(_clipboard.TryCopyWithExpiry(password)
                ? _localizer["Status.CopiedPassword"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    [RelayCommand]
    private void CopyFieldRow(object? row)
    {
        if (row is FieldRowVm field && field.Value.Length > 0)
        {
            SetStatus(_clipboard.TryCopyWithExpiry(field.Value)
                ? _localizer["Status.CopiedUsername"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    [RelayCommand]
    private void RevealFieldRow(object? row)
    {
        if (row is FieldRowVm { Masked: true } field)
        {
            // Toggle this row's reveal by re-issuing an unmasked copy in the list.
            var rows = DetailDataRows.ToList();
            var index = rows.IndexOf(field);
            if (index >= 0)
            {
                rows[index] = field with { Masked = false };
                DetailDataRows = rows;
            }
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        if (DetailWebsiteText.Length > 0 && CredentialValidation.IsSafeWebUrl(DetailWebsiteText))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DetailWebsiteText) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
            {
                SetStatus(_localizer["Error.OpenUrl"]);
            }
        }
        else
        {
            SetStatus(_localizer["Error.UnsafeUrl"]);
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedCredential is { } row)
        {
            await _items.ToggleFavoriteAsync(Session, row.Id);
            await LoadAsync();
        }
    }

    // ── organization commands ──

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
        {
            return;
        }

        try
        {
            await _folders.CreateAsync(Session, NewFolderName.Trim(), null);
            NewFolderName = string.Empty;
            await LoadOrganizationAsync();
        }
        catch (ValidationException ex)
        {
            SetStatus(_localizer["Validation." + ex.Message]);
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (SelectedFolderNav is { } folder)
        {
            await _folders.DeleteAsync(Session, folder.Id, deleteItems: false);
            if (FilterFolderId == folder.Id)
            {
                FilterFolderId = null;
            }

            await LoadOrganizationAsync();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        try
        {
            await _tags.CreateAsync(Session, NewTagName.Trim());
            NewTagName = string.Empty;
            await LoadOrganizationAsync();
        }
        catch (ValidationException ex)
        {
            SetStatus(_localizer["Validation." + ex.Message]);
        }
    }

    [RelayCommand]
    private async Task DeleteTagAsync()
    {
        if (SelectedTagNav is { } tag)
        {
            await _tags.DeleteAsync(Session, tag.Id);
            if (FilterTagId == tag.Id)
            {
                FilterTagId = null;
            }

            await LoadOrganizationAsync();
            await LoadAsync();
        }
    }

    // ── editor ──

    [RelayCommand]
    private void BeginCreate()
    {
        SelectedCredential = null;
        Detail = null;
        EditorKind = Section.StartsWith("Type:", StringComparison.Ordinal)
            && Enum.TryParse<ItemType>(Section[5..], out var kind) ? kind : ItemType.Login;
        ResetEditorFields();
        EditorVisible = true;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Detail is null)
        {
            return;
        }

        EditorKind = Detail.Kind;
        EditorTitle = Detail.Title;
        EditorUsername = DetailUsernameText;
        EditorWebsiteUrl = DetailWebsiteText;
        EditorNotes = string.Empty;
        EditorDescription = DetailNotesText;
        EditorFolderId = Detail.FolderId;
        EditorTagNames = string.Join(", ", Detail.TagIds.Select(id => _tagNames.GetValueOrDefault(id, id.ToString())));
        EditorDataFields.Clear();
        foreach (var spec in ItemFormRegistry.Fields[Detail.Kind])
        {
            if (!Detail.Data.TryGetValue(spec.DataKey, out var value) && spec.Input is not (InputKind.Boolean or InputKind.Date))
            {
                continue;
            }

            var text = value switch
            {
                System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.Array =>
                    string.Join('\n', el.EnumerateArray().Select(e => e.GetString() ?? string.Empty)),
                System.Text.Json.JsonElement el => el.ToString(),
                null => string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
            EditorDataFields.Add(new FieldEditorViewModel
            {
                DataKey = spec.DataKey, LabelKey = spec.LabelKey, Input = spec.Input, Value = text,
            });
        }

        EditorCustomFields.Clear();
        foreach (var field in Detail.CustomFields.OrderBy(f => f.Order))
        {
            EditorCustomFields.Add(new CustomFieldEditorViewModel
            {
                Id = Guid.TryParse(field.Id, out var parsed) ? parsed : Guid.NewGuid(),
                Label = field.Label,
                TypeIndex = Array.IndexOf(CustomFieldEditorViewModel.CustomFieldTypeValues, field.Type),
                Value = field.Value ?? string.Empty,
                Concealed = field.Concealed,
            });
        }

        EditorError = null;
        EditingId = Detail.Id;
        EditorVisible = true;
    }

    private void ResetEditorFields()
    {
        EditorTitle = EditorUsername = EditorWebsiteUrl = EditorNotes = string.Empty;
        EditorDescription = string.Empty;
        EditorFolderId = null;
        EditorTagNames = string.Empty;
        EditorDataFields.Clear();
        foreach (var spec in ItemFormRegistry.Fields[EditorKind])
        {
            EditorDataFields.Add(new FieldEditorViewModel
            {
                DataKey = spec.DataKey, LabelKey = spec.LabelKey, Input = spec.Input,
            });
        }

        EditorCustomFields.Clear();
        EditorError = null;
        EditingId = null;
    }

    [RelayCommand]
    private void AddCustomField() => EditorCustomFields.Add(new CustomFieldEditorViewModel());

    [RelayCommand]
    private void RemoveCustomField(CustomFieldEditorViewModel? row)
    {
        if (row is not null)
        {
            EditorCustomFields.Remove(row);
        }
    }

    [RelayCommand]
    private void MoveCustomFieldUp(CustomFieldEditorViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var index = EditorCustomFields.IndexOf(row);
        if (index > 0)
        {
            EditorCustomFields.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        EditorError = null;
        var data = EditorKind == ItemType.Login ? LoginDataFromLegacyFields() : DataFromDynamicRows();
        var customFields = EditorCustomFields.Select((row, index) => row.ToField(index)).ToList();
        var draft = new ItemDraft
        {
            Kind = EditorKind,
            Title = EditorTitle.Trim(),
            Description = JoinNonEmpty(EditorDescription, EditorKind == ItemType.Login ? EditorNotes : null),
            Data = data,
            CustomFields = customFields,
            FolderId = EditorFolderId,
            TagIds = await ResolveTagNamesAsync(EditorTagNames),
        };

        try
        {
            if (EditingId is { } id)
            {
                var updated = await _items.UpdateAsync(Session, id, draft);
                UpsertSummary(updated);
            }
            else
            {
                var created = await _items.CreateAsync(Session, draft);
                _allCredentials.Insert(0, created);
            }

            EditorVisible = false;
            EditorPasswordHandoff = [];
            EditorPasswordCleared?.Invoke(this, EventArgs.Empty);
            await ReloadAfterMutationAsync();
        }
        catch (ValidationException ex)
        {
            EditorError = _localizer["Validation." + ex.Message];
        }
        catch (StaleSessionException)
        {
            EditorVisible = false;
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private Dictionary<string, object?> LoginDataFromLegacyFields() => new()
    {
        ["username"] = NullIfEmpty(EditorUsername),
        ["password"] = EditorPasswordHandoff.Length == 0 ? KeepExistingPassword() : new string(EditorPasswordHandoff),
        ["urls"] = NullIfEmpty(EditorWebsiteUrl) is { } url ? new List<string> { url } : new List<string>(),
    };

    private string? KeepExistingPassword() =>
        EditingId is { } id ? Detail is { Id: var detailId } && detailId == id
            ? Detail.Data.GetValueOrDefault("password") as string
            : null : null;

    private Dictionary<string, object?> DataFromDynamicRows()
    {
        var data = new Dictionary<string, object?>();
        foreach (var row in EditorDataFields)
        {
            data[row.DataKey] = row.Input switch
            {
                InputKind.TextList => row.Value
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                InputKind.Boolean => row.BooleanValue ? "true" : "false",
                _ => string.IsNullOrWhiteSpace(row.Value) ? null : row.Value,
            };
        }

        return data;
    }

    private static string? JoinNonEmpty(params string?[] parts) =>
        string.Join('\n', parts.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } joined ? joined : null;

    private async Task<List<Guid>> ResolveTagNamesAsync(string names)
    {
        var resolved = new List<Guid>();
        foreach (var raw in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = TagNav.FirstOrDefault(t => string.Equals(t.Name, raw, StringComparison.OrdinalIgnoreCase))
                ?? TagNav.FirstOrDefault(t => TagNormalizer.Normalize(t.Name) == TagNormalizer.Normalize(raw));
            if (match is not null)
            {
                resolved.Add(match.Id);
                continue;
            }

            var created = await _tags.CreateAsync(Session, raw);
            resolved.Add(created.Id);
            TagNav.Add(new TagNavRow(created.Id, created.Name, 0));
        }

        return resolved;
    }

    [RelayCommand]
    private void CancelEditor()
    {
        EditorPasswordHandoff = [];
        EditorPasswordCleared?.Invoke(this, EventArgs.Empty);
        EditorVisible = false;
    }

    private void UpsertSummary(ItemSummary summary)
    {
        var existing = _allCredentials.FirstOrDefault(c => c.Id == summary.Id);
        if (existing is not null)
        {
            _allCredentials.Remove(existing);
        }

        _allCredentials.Insert(0, summary);
    }

    private async Task ReloadAfterMutationAsync()
    {
        var summaries = await _items.ListAsync(Session, BuildFilter());
        _allCredentials = summaries.ToList();
        var index = await _items.BuildSearchIndexAsync(Session, BuildFilter());
        _searchIndex = index.ToDictionary(e => e.Id, e => e.SearchText);
        RebuildRows(_allCredentials);
        ApplyFilter();
        await LoadOrganizationAsync();
        if (SelectedCredential is { } selected)
        {
            await LoadDetailAsync(selected.Id);
        }
    }

    // ── delete / trash ──

    [RelayCommand]
    private void BeginDelete()
    {
        if (SelectedCredential is not null)
        {
            _pendingPurgeId = null;
            DeleteConfirmVisible = true;
        }
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        DeleteConfirmVisible = false;
        if (SelectedCredential is not { } row)
        {
            return;
        }

        if (TrashView)
        {
            // In trash view the confirmation means permanent deletion.
            if (_pendingPurgeId is { } purgeId)
            {
                await _items.PurgeAsync(Session, purgeId);
            }
            else
            {
                await _items.PurgeAsync(Session, row.Id);
            }
        }
        else
        {
            await _items.TrashAsync(Session, row.Id);
        }

        var summary = _allCredentials.FirstOrDefault(c => c.Id == row.Id);
        if (summary is not null)
        {
            _allCredentials.Remove(summary);
        }

        RowsView.Remove(RowsView.FirstOrDefault(c => c.Id == row.Id)!);
        Detail = null;
        SelectedCredential = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void CancelDelete() => DeleteConfirmVisible = false;

    [RelayCommand]
    private async Task RestoreFromTrashAsync()
    {
        if (SelectedCredential is { } row)
        {
            await _items.RestoreAsync(Session, row.Id);
            SetStatus(_localizer["Status.Restored"]);
            await LoadAsync();
        }
    }


    [RelayCommand]
    private async Task BeginPurgeFromTrashAsync()
    {
        if (SelectedCredential is not null)
        {
            _pendingPurgeId = SelectedCredential.Id;
            DeleteConfirmVisible = true;
        }
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var removed = await _items.EmptyTrashAsync(Session, 0);
        SetStatus(string.Format(_localizer["Status.TrashEmptied"], removed));
        await LoadAsync();
    }

    // ── history ──

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (SelectedCredential is not { } row)
        {
            return;
        }

        Versions.Clear();
        foreach (var version in await _items.GetHistoryAsync(Session, row.Id))
        {
            Versions.Add(new VersionRowVm(version.Version, version.CreatedAt));
        }
    }

    [RelayCommand]
    private async Task RestoreVersionAsync()
    {
        if (SelectedCredential is { } row && SelectedVersion is { } version)
        {
            await _items.RestoreVersionAsync(Session, row.Id, version.Version);
            SetStatus(_localizer["Status.VersionRestored"]);
            await LoadAsync();
        }
    }

    // ── attachments ──

    [RelayCommand]
    private void BeginAddAttachment()
    {
        if (SelectedCredential is not null)
        {
            AddAttachmentRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Called by the shell after the file dialog picks a file.</summary>
    public async Task AddAttachmentFromFileAsync(string path)
    {
        if (SelectedCredential is { } row)
        {
            await _attachments.AddAsync(Session, row.Id, path);
            SetStatus(_localizer["Status.CopiedUsername"]);
            await LoadDetailAsync(row.Id);
        }
    }

    /// <summary>Called by the shell after the save dialog picks a destination.</summary>
    public async Task ExportAttachmentToFileAsync(string destination)
    {
        if (SelectedAttachment is { } attachment)
        {
            await _attachments.ExportAsync(Session, attachment.Id, destination);
            SetStatus(_localizer["Status.CopiedUsername"]);
        }
    }

    [RelayCommand]
    private void BeginExportAttachment()
    {
        if (SelectedAttachment is { } attachment)
        {
            ExportAttachmentRequested?.Invoke(this, (attachment.Id, attachment.Filename));
        }
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync()
    {
        if (SelectedAttachment is { } attachment)
        {
            await _attachments.DeleteAsync(Session, attachment.Id);
            Attachments.Remove(attachment);
        }
    }

    // ── lock ──

    [RelayCommand]
    private void Lock()
    {
        _sessions.LockCurrent();
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── session boundary ──

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RowsView.Clear();
        _allCredentials = [];
        _searchIndex = new Dictionary<Guid, string>();
        _folderNames = new Dictionary<Guid, string>();
        _tagNames = new Dictionary<Guid, string>();
        FolderNav.Clear();
        TagNav.Clear();
        Attachments.Clear();
        Versions.Clear();
        Detail = null;
        SelectedCredential = null;
        PasswordRevealed = false;
        SearchText = string.Empty;
        EditorVisible = false;
        DeleteConfirmVisible = false;
        VaultDisplayName = string.Empty;
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        _ = Task.Delay(4000).ContinueWith(_ => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = null));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
