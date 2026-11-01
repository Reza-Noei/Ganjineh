using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Import;
using Ganjineh.Application.Vaults;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>One preview-table row. Deliberately password-free: the preview model never sees the
/// secret column — those live in the import session until the write step (import spec §3).</summary>
public sealed partial class ImportRowViewModel : ObservableObject
{
    public int RowNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string OriginDisplay { get; init; } = string.Empty;
    public string SourceLabelKey { get; init; } = string.Empty;
    public ImportRecordStatus Status { get; init; }
    public string? InvalidReasonKey { get; init; }
    public bool IsDuplicate { get; init; }
    public string? DuplicateOfTitle { get; init; }
    public bool IsDuplicateWithinFile { get; init; }
    public Guid? ExistingItemId { get; init; }

    public bool IsSelectable => Status is ImportRecordStatus.Ready;
    public bool IsInvalid => Status is ImportRecordStatus.Invalid;

    [ObservableProperty] private bool _isSelected = true;

    /// <summary>Conflict action for duplicate rows (0=Skip, 1=Update existing, 2=Create copy);
    /// duplicates start on Skip so nothing is overwritten without explicit approval (§3/§8).</summary>
    [ObservableProperty] private int _conflictIndex;

    public ImportRowAction EffectiveAction => !IsSelectable || !IsSelected
        ? ImportRowAction.Skip
        : IsDuplicate
            ? ConflictIndex switch
            {
                1 => ImportRowAction.UpdateExisting,
                2 => ImportRowAction.CreateCopy,
                _ => ImportRowAction.Skip,
            }
            : ImportRowAction.Import;

    public string StatusText
    {
        get
        {
            var localizer = App.Services?.GetService(typeof(ILocalizer)) as ILocalizer;
            if (localizer is null)
            {
                return string.Empty;
            }

            if (IsInvalid)
            {
                return localizer[InvalidReasonKey ?? "Import.StatusInvalid"];
            }

            if (IsDuplicate)
            {
                return localizer["Import.StatusDuplicate"];
            }

            if (IsDuplicateWithinFile)
            {
                return localizer["Import.StatusRepeat"];
            }

            return localizer["Import.StatusReady"];
        }
    }
}

/// <summary>
/// Shared four-step import wizard (Settings → Import &amp; Export): instructions → file →
/// preview/conflicts → progress/results, with the selected browser preconfigured by the entry
/// command. The import session (password material) is kept out of all bindable state and
/// disposed on completion, cancellation or session loss; auto-lock/vault switching cancels the
/// run — per-record transactions keep exactly the records already written, and the summary
/// reports them truthfully (§9/§11).
/// </summary>
public sealed partial class BrowserImportViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserImportService _import;
    private readonly IVaultSessionManager _sessions;
    private readonly ILocalizer _localizer;

    private BrowserImportSession? _parsed;
    private CancellationTokenSource? _importCts;

    public BrowserImportViewModel(IBrowserImportService import, IVaultSessionManager sessions, ILocalizer localizer)
    {
        _import = import;
        _sessions = sessions;
        _localizer = localizer;
        _sessions.SessionChanged += OnSessionChanged;
    }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _step;          // 0 instructions, 1 file, 2 preview, 3 progress/results
    [ObservableProperty] private int _browserIndex;  // 0 chrome, 1 firefox

    // step 1
    [ObservableProperty] private string? _selectedFilePath;
    [ObservableProperty] private string? _fileError;
    [ObservableProperty] private bool _isParsing;

    // step 2
    [ObservableProperty] private string _filterText = string.Empty;

    // step 3
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int _processed;
    [ObservableProperty] private int _total;
    [ObservableProperty] private string? _resultSummary;
    [ObservableProperty] private string? _resultFailures;
    [ObservableProperty] private bool _hasWrittenItems;

    public ObservableCollection<ImportRowViewModel> RowsView { get; } = [];

    /// <summary>Shell opens the Win32 file dialog; the chosen path arrives via SetSelectedFile.</summary>
    public event EventHandler? PickFileRequested;

    /// <summary>Post-import navigation: the shell selects the item in the workspace.</summary>
    public event EventHandler<Guid>? OpenItemRequested;

    /// <summary>Post-import cleanup help: the shell opens the source CSV's containing folder
    /// (§13). The file itself is never touched by the app.</summary>
    public event EventHandler<string>? OpenSourceFolderRequested;

    public ImportBrowser Browser => BrowserIndex == 1 ? ImportBrowser.Firefox : ImportBrowser.Chrome;

    public bool IsInstructionsStep => Step == 0;
    public bool IsFileStep => Step == 1;
    public bool IsPreviewStep => Step == 2;
    public bool IsProgressStep => Step == 3;

    public string WizardTitle => _localizer[
        Browser == ImportBrowser.Firefox ? "Import.WizardTitleFirefox" : "Import.WizardTitleChrome"];

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsInstructionsStep));
        OnPropertyChanged(nameof(IsFileStep));
        OnPropertyChanged(nameof(IsPreviewStep));
        OnPropertyChanged(nameof(IsProgressStep));
    }

    // ── open/close ──

    [RelayCommand]
    private void OpenBrowser(string parameter)
    {
        BrowserIndex = string.Equals(parameter, "firefox", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        OnPropertyChanged(nameof(WizardTitle));
        ResetToStart();
        IsVisible = true;
    }

    private void ResetToStart()
    {
        Step = 0;
        FileError = null;
        SelectedFilePath = null;
        FilterText = string.Empty;
        ResultSummary = null;
        ResultFailures = null;
        HasWrittenItems = false;
        FirstWrittenItemId = null;
        RowsView.Clear();
        DisposeSession();
    }

    [RelayCommand]
    private void Close()
    {
        _importCts?.Cancel();
        IsVisible = false;
        DisposeSession();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // Vault locked or switched under us: stop writing immediately and drop the wizard.
        if (IsVisible)
        {
            _importCts?.Cancel();
            IsVisible = false;
            DisposeSession();
        }
    }

    // ── step 0 → 1 ──

    [RelayCommand]
    private void BeginFileSelection() => Step = 1;

    [RelayCommand]
    private void BrowseForFile() => PickFileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Shell hands the picked file path here.</summary>
    public void SetSelectedFile(string path)
    {
        FileError = null;
        SelectedFilePath = path;
    }

    // ── step 1 → 2: parse ──

    [RelayCommand]
    private async Task ParseFileAsync()
    {
        if (SelectedFilePath is null || IsParsing || _importCts is { })
        {
            return;
        }

        FileError = null;
        IsParsing = true;
        try
        {
            var session = _sessions.Current ?? throw new StaleSessionException("No active vault session.");
            var parsed = await Task.Run(() => _import.Parse(session, Browser, SelectedFilePath));
            _parsed?.Dispose();
            _parsed = parsed;
            RebuildRows();
            Step = 2;
        }
        catch (ImportFormatException ex)
        {
            FileError = LocalizeError(ex.ErrorKey, ex.Detail);
        }
        catch (ImportSchemaException ex)
        {
            FileError = LocalizeError(ex.ErrorKey, ex.Detail);
        }
        catch (StaleSessionException)
        {
            FileError = _localizer["Error.SessionLost"];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileError = _localizer["Import.ErrorReadFailed"];
        }
        finally
        {
            IsParsing = false;
        }
    }

    private void RebuildRows()
    {
        var rows = _parsed?.Rows ?? [];
        var query = SearchNormalizer.Normalize(FilterText);
        RowsView.Clear();
        foreach (var row in rows)
        {
            if (query.Length > 0
                && !SearchNormalizer.Matches(row.Title, query)
                && !SearchNormalizer.Matches(row.Username, query)
                && !SearchNormalizer.Matches(row.OriginDisplay, query))
            {
                continue;
            }

            RowsView.Add(new ImportRowViewModel
            {
                RowNumber = row.RowNumber,
                Title = row.Title,
                Username = row.Username,
                OriginDisplay = row.OriginDisplay,
                SourceLabelKey = row.SourceLabelKey,
                Status = row.Status,
                InvalidReasonKey = row.InvalidReasonKey,
                IsDuplicate = row.IsDuplicate,
                DuplicateOfTitle = row.DuplicateOfTitle,
                IsDuplicateWithinFile = row.IsDuplicateWithinFile,
                ExistingItemId = row.ExistingItemId,
                IsSelected = row.Status is ImportRecordStatus.Ready,
                ConflictIndex = 0, // duplicates default to Skip (§3/§8)
            });
        }

        OnPropertyChanged(nameof(CountsLine));
        OnPropertyChanged(nameof(UnsupportedLine));
        OnPropertyChanged(nameof(HasRows));
    }

    partial void OnFilterTextChanged(string value) => RebuildRows();

    public string CountsLine
    {
        get
        {
            var rows = _parsed?.Rows ?? [];
            return string.Format(
                _localizer["Import.CountsLine"],
                rows.Count(r => r.Status is ImportRecordStatus.Ready),
                rows.Count(r => r.IsDuplicate),
                rows.Count(r => r.Status is ImportRecordStatus.Invalid),
                _parsed?.EmptyRowCount ?? 0,
                _parsed?.MalformedRowCount ?? 0);
        }
    }

    public string? UnsupportedLine
    {
        get
        {
            var unsupported = _parsed?.UnsupportedColumns ?? [];
            return unsupported.Count == 0
                ? null
                : string.Format(_localizer["Import.UnsupportedColumns"], string.Join("، ", unsupported));
        }
    }

    public bool HasRows => RowsView.Count > 0;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in RowsView.Where(r => r.IsSelectable))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var row in RowsView)
        {
            row.IsSelected = false;
        }
    }

    // ── step 2 → 3: execute ──

    [RelayCommand]
    private void BeginImport()
    {
        if (_parsed is null || IsImporting || _sessions.Current is not { } session)
        {
            return;
        }

        var plans = RowsView
            .Where(r => r.IsSelected)
            .Select(r => new ImportRowPlan(r.RowNumber, r.EffectiveAction, r.ExistingItemId))
            .ToList();
        if (plans.Count == 0)
        {
            return;
        }

        Step = 3;
        IsImporting = true;
        Processed = 0;
        Total = plans.Count;
        ResultSummary = null;
        ResultFailures = null;
        HasWrittenItems = false;
        FirstWrittenItemId = null;
        _importCts = new CancellationTokenSource();
        var cts = _importCts;

        var progress = new Progress<ImportProgress>(p =>
        {
            Processed = p.Processed;
            Total = p.Total;
        });
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        async Task RunImportAsync()
        {
            ImportRunResult result;
            try
            {
                result = await _import.ExecuteAsync(session, _parsed!, plans, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = new ImportRunResult
                {
                    Imported = 0, Updated = 0, Skipped = 0, Failed = 0, Total = plans.Count,
                    FailureReasons = new Dictionary<int, string>(), WrittenItems = new Dictionary<int, Guid>(),
                    StoppedEarly = true,
                };
            }
            catch (Exception)
            {
                // Unexpected infrastructure failure (disk, database): never leave the wizard
                // spinning — report an early stop with the counts that were already reported.
                result = new ImportRunResult
                {
                    Imported = 0, Updated = 0, Skipped = 0, Failed = 0, Total = plans.Count,
                    FailureReasons = new Dictionary<int, string>(), WrittenItems = new Dictionary<int, Guid>(),
                    StoppedEarly = true,
                };
            }

            if (!IsVisible)
            {
                _importCts = null;
                return; // session changed mid-run; the wizard is already gone
            }

            ShowResult(result);
            IsImporting = false;
            _importCts = null; // allow the next wizard run to parse again
        }

        if (dispatcher is null)
        {
            // Non-WPF context (tests): no bindings attached and no lock interleave to respect.
            _ = Task.Run(() => RunImportAsync());
            return;
        }

        // Run on the dispatcher: each record's write is serialized with lock/vault-switch on
        // the UI thread (no mid-insert race with session teardown), while the awaits between
        // records keep the shell responsive (§9/§11).
        _ = dispatcher.InvokeAsync(async () => await RunImportAsync());
    }

    private void ShowResult(ImportRunResult result)
    {
        var summary = result.StoppedEarly
            ? _localizer["Import.ResultStopped"]
            : _localizer["Import.ResultDone"];
        ResultSummary = summary + "\n" + string.Format(
            _localizer["Import.ResultCounts"],
            result.Imported, result.Updated, result.Skipped, result.Failed);

        if (result.FailureReasons.Count > 0)
        {
            ResultFailures = string.Join("\n", result.FailureReasons
                .OrderBy(kv => kv.Key)
                .Select(kv => string.Format(_localizer["Import.FailureLine"], kv.Key, _localizer[kv.Value])));
        }

        HasWrittenItems = result.WrittenItems.Count > 0;
        // First written item in plan (row) order — deterministic navigation target.
        FirstWrittenItemId = result.WrittenItems.OrderBy(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
        OnPropertyChanged(nameof(CanOpenSourceFolder));
    }

    private Guid? FirstWrittenItemId { get; set; }

    /// <summary>Open-folder offer visibility: only after a successful run, while the parsed
    /// session (which remembers the source path) is still alive.</summary>
    public bool CanOpenSourceFolder => !IsImporting && HasWrittenItems && _parsed is not null;

    partial void OnIsImportingChanged(bool value) => OnPropertyChanged(nameof(CanOpenSourceFolder));

    [RelayCommand]
    private void OpenSourceFolder()
    {
        if (_parsed is { } parsed)
        {
            OpenSourceFolderRequested?.Invoke(this, parsed.SourceFilePath);
        }
    }

    [RelayCommand]
    private void ShowImportedItem()
    {
        var id = FirstWrittenItemId;
        CloseCommand.Execute(null);
        if (id is { } itemId)
        {
            OpenItemRequested?.Invoke(this, itemId);
        }
    }

    private void DisposeSession()
    {
        // Best-effort teardown of the held password material: managed string contents cannot be
        // reliably zeroized (secure-memory-limitations.md); the session nulls its references.
        _parsed?.Dispose();
        _parsed = null;
    }

    private string LocalizeError(string errorKey, string? detail)
    {
        var text = _localizer[errorKey];
        if (text.StartsWith('['))
        {
            return errorKey; // untranslated key — show the key, never raw file content
        }

        return detail is not null ? text + " (" + detail + ")" : text;
    }

    public void Dispose()
    {
        _importCts?.Cancel();
        DisposeSession();
        _sessions.SessionChanged -= OnSessionChanged;
    }
}
