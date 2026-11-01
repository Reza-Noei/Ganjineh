using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Desktop.Services;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>
/// Settings overlay: language (fa/en), theme (Light/Dark), clipboard lifetime, auto-lock delay.
/// Every change applies immediately and persists — the underlying services
/// (<see cref="LocalizationService"/> via <see cref="ILocalizer"/>, <see cref="ThemeService"/>,
/// <see cref="AutoLockService"/>) all read the live values. No secrets here (ADR-006).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly int[] AutoLockChoices = [0, 1, 5, 10, 15, 30];

    private readonly IAppSettingsStore _settings;
    private readonly ILocalizer _localizer;
    private readonly ThemeService _theme;

    /// <summary>Guards against re-persisting while Load seeds the properties.</summary>
    private bool _seeding;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _languageIndex;   // 0 = فارسی, 1 = English
    [ObservableProperty] private int _themeIndex;      // 0 = Light, 1 = Dark
    [ObservableProperty] private int _clipboardSeconds;
    [ObservableProperty] private int _autoLockIndex;

    public SettingsViewModel(IAppSettingsStore settings, ILocalizer localizer, ThemeService theme)
    {
        _settings = settings;
        _localizer = localizer;
        _theme = theme;
        Load();
    }

    public static IReadOnlyList<int> AutoLockValues => AutoLockChoices;

    private void Load()
    {
        _seeding = true;
        var current = _settings.Current;
        LanguageIndex = current.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ThemeIndex = string.Equals(current.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ClipboardSeconds = Math.Clamp(current.ClipboardTimeoutSeconds, 10, 120);
        var lockIndex = Array.IndexOf(AutoLockChoices, current.AutoLockMinutes);
        AutoLockIndex = lockIndex < 0 ? 3 : lockIndex; // default preset = 10 minutes
        _seeding = false;
    }

    partial void OnLanguageIndexChanged(int value) => Apply();
    partial void OnThemeIndexChanged(int value) => Apply();
    partial void OnClipboardSecondsChanged(int value) => Apply();
    partial void OnAutoLockIndexChanged(int value) => Apply();

    private void Apply()
    {
        if (_seeding)
        {
            return;
        }

        var current = _settings.Current;
        var updated = new AppSettings
        {
            Language = LanguageIndex == 1 ? "en" : "fa",
            Theme = ThemeIndex == 1 ? "Dark" : "Light",
            FirstRunCompleted = current.FirstRunCompleted,
            ClipboardTimeoutSeconds = Math.Clamp(ClipboardSeconds, 10, 120),
            AutoLockMinutes = AutoLockChoices[Math.Clamp(AutoLockIndex, 0, AutoLockChoices.Length - 1)],
        };
        _settings.Update(updated);
        _localizer.SetCulture(updated.Language);
        _theme.Apply(updated.Theme);
    }

    [RelayCommand]
    private void Open()
    {
        Load();
        IsVisible = true;
    }

    [RelayCommand]
    private void Close() => IsVisible = false;
}
