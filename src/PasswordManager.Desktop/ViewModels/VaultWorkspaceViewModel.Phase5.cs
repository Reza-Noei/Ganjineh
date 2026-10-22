using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Generation;
using PasswordManager.Desktop.Services;

namespace PasswordManager.Desktop.ViewModels;

/// <summary>
/// Phase 5 workspace additions: generator overlay state, security center dashboard, TOTP display.
/// </summary>
public partial class VaultWorkspaceViewModel
{
    private bool _generatorInitialized;

    // ── Generator state ──
    [ObservableProperty] private bool _generatorVisible;
    [ObservableProperty] private bool _passphraseMode;
    [ObservableProperty] private int _generatorLength = 16;
    [ObservableProperty] private bool _genLowercase = true;
    [ObservableProperty] private bool _genUppercase = true;
    [ObservableProperty] private bool _genDigits = true;
    [ObservableProperty] private bool _genSymbols;
    [ObservableProperty] private bool _genExcludeAmbiguous;
    [ObservableProperty] private string _genExcludedCharacters = string.Empty;
    [ObservableProperty] private string _generatedPassword = string.Empty;
    [ObservableProperty] private string _generatorEntropyText = string.Empty;
    [ObservableProperty] private int _passphraseWordCount = 5;
    [ObservableProperty] private string _passphraseSeparator = "-";
    [ObservableProperty] private bool _passphraseCapitalize;
    [ObservableProperty] private bool _passphraseNumber;
    [ObservableProperty] private string _generatedPassphrase = string.Empty;
    [ObservableProperty] private string _passphraseEntropyText = string.Empty;
    [ObservableProperty] private string _generatorStrengthText = string.Empty;
    [ObservableProperty] private bool _generatorTargetIsEditor;

    public event EventHandler? GeneratorUseInEditorRequested;

    // ── Security center state ──
    [ObservableProperty] private VaultSecurityReport? _securityReport;
    [ObservableProperty] private bool _securityAnalysisBusy;
    [ObservableProperty] private FindingKind? _findingFilter;

    public ObservableCollection<SecurityFinding> FindingsView { get; } = [];

    public bool SecuritySectionActive => Section == "Security";

    public string SecurityScoreText => SecurityReport is null
        ? string.Empty
        : $"{SecurityReport.Score} / 100";

    public int TotalLogins => SecurityReport?.TotalLogins ?? 0;
    public int WeakCount => SecurityReport?.Count(FindingKind.WeakPassword) ?? 0;
    public int ReusedCount => SecurityReport?.Count(FindingKind.ReusedPassword) ?? 0;
    public int DuplicateCount => SecurityReport?.Count(FindingKind.DuplicateAccount) ?? 0;
    public int EmptyCount => SecurityReport?.Count(FindingKind.EmptyPassword) ?? 0;
    public int OldCount => SecurityReport?.Count(FindingKind.OldPassword) ?? 0;
    public int ExpiringCount => SecurityReport?.Count(FindingKind.ExpiringSecret) ?? 0;

    // ── TOTP display state ──
    [ObservableProperty] private string _totpCode = string.Empty;
    [ObservableProperty] private int _totpSecondsRemaining;
    [ObservableProperty] private bool _totpAvailable;

    private System.Windows.Threading.DispatcherTimer? _totpTimer;

    public bool DetailHasTotp => Detail is not null
        && TotpConfig.FromData(NormalizedDetailData()) is not null;

    private IReadOnlyDictionary<string, object?> NormalizedDetailData()
    {
        if (Detail is null)
        {
            return new Dictionary<string, object?>();
        }

        var normalized = new Dictionary<string, object?>(Detail.Data);
        var service = new VaultItemService();
        service.NormalizeForDisplay(normalized);
        return normalized;
    }

    partial void OnGeneratorLengthChanged(int value) => GenerateNow();

    partial void OnGenLowercaseChanged(bool value) => GenerateNow();
    partial void OnGenUppercaseChanged(bool value) => GenerateNow();
    partial void OnGenDigitsChanged(bool value) => GenerateNow();
    partial void OnGenSymbolsChanged(bool value) => GenerateNow();
    partial void OnGenExcludeAmbiguousChanged(bool value) => GenerateNow();
    partial void OnPassphraseModeChanged(bool value) => GenerateNow();
    partial void OnPassphraseWordCountChanged(int value) => GenerateNow();
    partial void OnPassphraseSeparatorChanged(string value) => GenerateNow();
    partial void OnPassphraseCapitalizeChanged(bool value) => GenerateNow();
    partial void OnPassphraseNumberChanged(bool value) => GenerateNow();

    // ── commands ──

    [RelayCommand]
    private void OpenGenerator()
    {
        GeneratorVisible = true;
        if (!_generatorInitialized)
        {
            GenerateNow();
            _generatorInitialized = true;
        }
    }

    [RelayCommand]
    private void CloseGenerator() => GeneratorVisible = false;

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        var options = Enum.TryParse<PasswordPreset>(preset, out var parsed)
            ? PasswordGenerationOptions.ForPreset(parsed)
            : new PasswordGenerationOptions();
        GeneratorLength = options.Length;
        GenLowercase = (options.Classes & CharacterClasses.Lowercase) != 0;
        GenUppercase = (options.Classes & CharacterClasses.Uppercase) != 0;
        GenDigits = (options.Classes & CharacterClasses.Digits) != 0;
        GenSymbols = (options.Classes & CharacterClasses.Symbols) != 0;
        GenExcludeAmbiguous = options.ExcludeAmbiguous;
        PassphraseMode = preset == nameof(PasswordPreset.Custom) && PassphraseMode;
        GenerateNow();
    }

    [RelayCommand]
    private void GenerateNow()
    {
        try
        {
            if (PassphraseMode)
            {
                var passphrase = _passphraseGenerator.Generate(new PassphraseOptions
                {
                    WordCount = Math.Clamp(PassphraseWordCount, 3, 12),
                    Separator = string.IsNullOrEmpty(PassphraseSeparator) ? "-" : PassphraseSeparator,
                    Capitalize = PassphraseCapitalize,
                    AppendNumber = PassphraseNumber,
                });
                GeneratedPassphrase = passphrase.Value;
                PassphraseEntropyText = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "{0:0.#} bit", passphrase.EntropyBits);
                GeneratorStrengthText = _estimator.Evaluate(passphrase.Value.ToCharArray()).Score switch
                {
                    StrengthScore.VeryWeak or StrengthScore.Weak => _localizer["Strength.Warn.Common"],
                    _ => string.Empty,
                };
            }
            else
            {
                var password = _passwordGenerator.Generate(new PasswordGenerationOptions
                {
                    Length = Math.Clamp(GeneratorLength, 4, 256),
                    Classes =
                        (GenLowercase ? CharacterClasses.Lowercase : 0)
                        | (GenUppercase ? CharacterClasses.Uppercase : 0)
                        | (GenDigits ? CharacterClasses.Digits : 0)
                        | (GenSymbols ? CharacterClasses.Symbols : 0),
                    ExcludeAmbiguous = GenExcludeAmbiguous,
                    ExcludedCharacters = string.IsNullOrWhiteSpace(GenExcludedCharacters) ? null : GenExcludedCharacters,
                });
                GeneratedPassword = password.Value;
                GeneratorEntropyText = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "{0:0.#} bit", password.EntropyBits);
                GeneratorStrengthText = _estimator.Evaluate(password.Value.ToCharArray()).Score switch
                {
                    StrengthScore.VeryWeak or StrengthScore.Weak or StrengthScore.Fair =>
                        _localizer["Strength.Warn.Common"],
                    _ => string.Empty,
                };
            }
        }
        catch (GenerationException ex)
        {
            GeneratorStrengthText = _localizer["Validation." + ex.Message];
        }
    }

    [RelayCommand]
    private void CopyGeneratedPassword()
    {
        if (PassphraseMode ? GeneratedPassphrase.Length > 0 : GeneratedPassword.Length > 0)
        {
            var value = PassphraseMode ? GeneratedPassphrase : GeneratedPassword;
            SetStatus(_clipboard.TryCopyWithExpiry(value)
                ? _localizer["Status.CopiedPassword"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    /// <summary>Hands the generated value to the open editor's password field (explicit action).</summary>
    [RelayCommand]
    private void UseInEditor()
    {
        EditorPasswordHandoff = (PassphraseMode ? GeneratedPassphrase : GeneratedPassword).ToCharArray();
        GeneratorUseInEditorRequested?.Invoke(this, EventArgs.Empty);
        GeneratorVisible = false;
    }

    // ── security center ──

    [RelayCommand]
    private void OpenSecurityCenter()
    {
        Section = "Security";
        _ = RunSecurityAnalysisAsync();
    }

    [RelayCommand]
    private async Task RunSecurityAnalysisAsync()
    {
        if (!_sessions.HasActiveSession || SecurityAnalysisBusy)
        {
            return;
        }

        SecurityAnalysisBusy = true;
        try
        {
            SecurityReport = await _analyzer.AnalyzeAsync(Session);
            FindingsView.Clear();
            foreach (var finding in SecurityReport.Findings
                         .Where(f => FindingFilter is null || f.Kind == FindingFilter))
            {
                FindingsView.Add(finding);
            }

            OnPropertyChanged(nameof(SecurityScoreText));
            OnPropertyChanged(nameof(TotalLogins));
            OnPropertyChanged(nameof(WeakCount));
            OnPropertyChanged(nameof(ReusedCount));
            OnPropertyChanged(nameof(DuplicateCount));
            OnPropertyChanged(nameof(EmptyCount));
            OnPropertyChanged(nameof(OldCount));
            OnPropertyChanged(nameof(ExpiringCount));
        }
        catch (StaleSessionException)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            SecurityAnalysisBusy = false;
        }
    }

    [RelayCommand]
    private void FilterFindings(string? kind)
    {
        FindingFilter = Enum.TryParse<FindingKind>(kind, out var parsed) ? parsed : null;
        FindingsView.Clear();
        if (SecurityReport is null)
        {
            return;
        }

        foreach (var finding in SecurityReport.Findings
                     .Where(f => FindingFilter is null || f.Kind == FindingFilter))
        {
            FindingsView.Add(finding);
        }
    }

    [RelayCommand]
    private async Task OpenFindingItemAsync(SecurityFinding? finding)
    {
        if (finding is null)
        {
            return;
        }

        Section = "All";
        await LoadAsync();
        var row = RowsView.FirstOrDefault(r => r.Id == finding.ItemId);
        if (row is not null)
        {
            SelectedCredential = row;
        }
    }

    // ── TOTP display ──

    [RelayCommand]
    private void CopyTotp()
    {
        if (TotpCode.Length > 0)
        {
            SetStatus(_clipboard.TryCopyWithExpiry(TotpCode)
                ? _localizer["Status.CopiedPassword"]
                : _localizer["Status.ClipboardFailed"]);
        }
    }

    private void StartTotpTimer()
    {
        if (_totpTimer is not null)
        {
            return;
        }

        _totpTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _totpTimer.Tick += (_, _) => RefreshTotp();
        _totpTimer.Start();
    }

    public void RefreshTotp()
    {
        if (!_sessions.HasActiveSession || Detail is null)
        {
            TotpAvailable = false;
            TotpCode = string.Empty;
            return;
        }

        var config = TotpConfig.FromData(NormalizedDetailData());
        if (config is null)
        {
            TotpAvailable = false;
            TotpCode = string.Empty;
            return;
        }

        TotpAvailable = true;
        StartTotpTimer();
        var result = _otp.TotpNow(Detail.Data, DateTimeOffset.UtcNow);
        TotpCode = result?.Code ?? string.Empty;
        TotpSecondsRemaining = result?.SecondsRemaining ?? 0;
    }
}
