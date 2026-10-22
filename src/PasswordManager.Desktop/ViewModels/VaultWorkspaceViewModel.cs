using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Application.Abstractions;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.VaultHeader;
using PasswordManager.Desktop.Services;

namespace PasswordManager.Desktop.ViewModels;

/// <summary>Row for the center list: never carries the password.</summary>
public sealed class CredentialRowViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? WebsiteUrl { get; init; }
    public bool Favorite { get; set; }
}

public partial class VaultWorkspaceViewModel : ObservableObject
{
    private readonly IVaultLifecycle _lifecycle;
    private readonly IVaultSessionManager _sessions;
    private readonly ICredentialService _credentials;
    private readonly ClipboardService _clipboard;
    private readonly ILocalizer _localizer;
    private readonly IVaultCatalog _catalog;

    public VaultWorkspaceViewModel(
        IVaultLifecycle lifecycle,
        IVaultSessionManager sessions,
        ICredentialService credentials,
        ClipboardService clipboard,
        ILocalizer localizer,
        IVaultCatalog catalog)
    {
        _lifecycle = lifecycle;
        _sessions = sessions;
        _credentials = credentials;
        _clipboard = clipboard;
        _localizer = localizer;
        _catalog = catalog;
        _sessions.SessionChanged += OnSessionChanged;
    }

    public VaultSession Session => _sessions.Current ?? throw new InvalidOperationException("No active vault session.");

    [ObservableProperty] private string _vaultDisplayName = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private CredentialRowViewModel? _selectedCredential;
    [ObservableProperty] private CredentialDetail? _detail;
    [ObservableProperty] private bool _passwordRevealed;
    [ObservableProperty] private bool _editorVisible;
    [ObservableProperty] private bool _deleteConfirmVisible;

    // Editor fields (title/username/url/notes are not secret; the password travels via
    // the shell's PasswordBox handoff, never through a bound string property).
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _editorUsername = string.Empty;
    [ObservableProperty] private string _editorWebsiteUrl = string.Empty;
    [ObservableProperty] private string _editorNotes = string.Empty;
    [ObservableProperty] private string? _editorError;

    public ObservableCollection<CredentialRowViewModel> Credentials { get; } = [];
    private List<CredentialSummary> _allCredentials = [];

    public event EventHandler? LockRequested;
    private CancellationTokenSource? _searchDebounce;

    public string PasswordDisplay => PasswordRevealed && Detail is not null ? Detail.Password ?? string.Empty : "••••••••";

    partial void OnPasswordRevealedChanged(bool value) => OnPropertyChanged(nameof(PasswordDisplay));

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

            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyFilter());
        }, token);
    }

    public async Task LoadAsync()
    {
        if (!_sessions.HasActiveSession)
        {
            return;
        }

        var session = _sessions.Current ?? throw new InvalidOperationException("No active vault session.");
        var name = _lifecycle.GetVaultName(session);
        VaultDisplayName = string.IsNullOrWhiteSpace(name) ? session.VaultIdText : name;
        IsBusy = true;
        HasError = false;
        try
        {
            var all = await _credentials.ListAsync(Session);
            _allCredentials = all.ToList();
            var index = await _credentials.BuildSearchIndexAsync(Session);
            _searchIndex = index.ToDictionary(e => e.Id, e => e.SearchText);
            RebuildRows(_allCredentials);
            ApplyFilter();
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

    private void RebuildRows(IEnumerable<CredentialSummary> summaries)
    {
        Credentials.Clear();
        foreach (var s in summaries)
        {
            Credentials.Add(new CredentialRowViewModel
            {
                Id = s.Id, Title = s.Title, Username = s.Username, WebsiteUrl = s.WebsiteUrl, Favorite = s.Favorite,
            });
        }
    }

    private void ApplyFilter()
    {
        var query = SearchNormalizer.Normalize(SearchText);
        var filtered = _allCredentials
            .Where(s => query.Length == 0
                        || (_searchIndex.TryGetValue(s.Id, out var text)
                            && SearchNormalizer.Matches(text, query))
                        || SearchNormalizer.Matches(s.Title, query)
                        || SearchNormalizer.Matches(s.Username, query)
                        || SearchNormalizer.Matches(s.WebsiteUrl, query))
            .Select(s => new CredentialRowViewModel
            {
                Id = s.Id,
                Title = s.Title,
                Username = s.Username,
                WebsiteUrl = s.WebsiteUrl,
                Favorite = s.Favorite,
            });

        RowsView.Clear();
        foreach (var row in filtered)
        {
            RowsView.Add(row);
        }

        OnPropertyChanged(nameof(HasNoResults));
    }

    public ObservableCollection<CredentialRowViewModel> RowsView { get; } = [];

    private Dictionary<Guid, string> _searchIndex = new();

    public bool HasNoResults => RowsView.Count == 0 && !IsBusy && !HasError;

    partial void OnSelectedCredentialChanged(CredentialRowViewModel? value)
    {
        if (value is null)
        {
            Detail = null;
            return;
        }

        _ = LoadDetailAsync(value.Id);
    }

    private async Task LoadDetailAsync(Guid id)
    {
        try
        {
            PasswordRevealed = false;
            Detail = await _credentials.GetAsync(Session, id);
        }
        catch (StaleSessionException)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or PasswordManager.Persistence.DatabaseCorruptionException)
        {
            HasError = true;
        }
    }

    [RelayCommand]
    private void ToggleReveal() => PasswordRevealed = !PasswordRevealed;

    [RelayCommand]
    private void CopyUsername()
    {
        if (Detail?.Username is { Length: > 0 } username)
        {
            SetStatus(_clipboard.TryCopyWithExpiry(username)
                ? _localizer["Status.CopiedUsername"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (Detail is { Password: { } password })
        {
            SetStatus(_clipboard.TryCopyWithExpiry(password)
                ? _localizer["Status.CopiedPassword"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        if (Detail?.WebsiteUrl is { Length: > 0 } url && CredentialValidation.IsSafeWebUrl(url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
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
    private void BeginCreate()
    {
        EditorTitle = EditorUsername = EditorWebsiteUrl = EditorNotes = string.Empty;
        EditorPasswordHandoff = [];
        EditingId = null;
        EditorError = null;
        EditorVisible = true;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Detail is null)
        {
            return;
        }

        EditorTitle = Detail.Title;
        EditorUsername = Detail.Username ?? string.Empty;
        EditorWebsiteUrl = Detail.WebsiteUrl ?? string.Empty;
        EditorNotes = Detail.Notes ?? string.Empty;
        EditorPasswordHandoff = [];
        EditingId = Detail.Id;
        EditorError = null;
        EditorVisible = true;
    }

    /// <summary>Password handed over by the editor's PasswordBox (code-behind), never bound.</summary>
    public char[] EditorPasswordHandoff { get; set; } = [];

    public Guid? EditingId { get; private set; }

    public event EventHandler? EditorPasswordCleared;

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        EditorError = null;
        var draft = new CredentialDraft
        {
            Title = EditorTitle.Trim(),
            Username = NullIfEmpty(EditorUsername),
            Password = EditorPasswordHandoff.Length == 0 ? null : new string(EditorPasswordHandoff),
            WebsiteUrl = NullIfEmpty(EditorWebsiteUrl),
            Notes = NullIfEmpty(EditorNotes),
        };

        try
        {
            if (EditingId is { } id)
            {
                var current = await _credentials.GetAsync(Session, id);
                if (EditorPasswordHandoff.Length == 0)
                {
                    draft = draft with { Password = current.Password };
                }

                var updated = await _credentials.UpdateAsync(Session, id, draft);
                ReplaceRow(updated);
            }
            else
            {
                var created = await _credentials.CreateAsync(Session, draft);
                _allCredentials.Insert(0, created);
                ReplaceRow(created);
            }

            EditorVisible = false;
            Array.Clear(EditorPasswordHandoff);
            EditorPasswordHandoff = [];
            EditorPasswordCleared?.Invoke(this, EventArgs.Empty);
            ApplyFilter();
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

    [RelayCommand]
    private void CancelEditor()
    {
        Array.Clear(EditorPasswordHandoff);
        EditorPasswordHandoff = [];
        EditorVisible = false;
    }

    [RelayCommand]
    private void BeginDelete()
    {
        if (SelectedCredential is not null)
        {
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

        try
        {
            await _credentials.DeleteAsync(Session, row.Id);
            var summary = _allCredentials.FirstOrDefault(c => c.Id == row.Id);
            if (summary is not null)
            {
                _allCredentials.Remove(summary);
            }

            Credentials.Remove(Credentials.FirstOrDefault(c => c.Id == row.Id)!);
            RowsView.Remove(RowsView.FirstOrDefault(c => c.Id == row.Id)!);
            Detail = null;
            SelectedCredential = null;
            ApplyFilter();
        }
        catch (StaleSessionException)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void CancelDelete() => DeleteConfirmVisible = false;

    [RelayCommand]
    private void Lock()
    {
        _sessions.LockCurrent();
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SwitchVault() => Lock();

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // Session boundary: wipe anything that could hold secrets from the previous session.
        Credentials.Clear();
        RowsView.Clear();
        _allCredentials = [];
        _searchIndex = new Dictionary<Guid, string>();
        Detail = null;
        SelectedCredential = null;
        PasswordRevealed = false;
        SearchText = string.Empty;
        EditorVisible = false;
        DeleteConfirmVisible = false;
        VaultDisplayName = string.Empty;
    }

    private void ReplaceRow(CredentialSummary summary)
    {
        var existing = _allCredentials.FirstOrDefault(c => c.Id == summary.Id);
        if (existing is not null)
        {
            _allCredentials.Remove(existing);
        }

        _allCredentials.Insert(0, summary);
        var row = Credentials.FirstOrDefault(c => c.Id == summary.Id);
        if (row is not null)
        {
            Credentials.Remove(row);
        }

        Credentials.Insert(0, new CredentialRowViewModel
        {
            Id = summary.Id, Title = summary.Title, Username = summary.Username,
            WebsiteUrl = summary.WebsiteUrl, Favorite = summary.Favorite,
        });
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        _ = Task.Delay(4000).ContinueWith(_ => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = null));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
