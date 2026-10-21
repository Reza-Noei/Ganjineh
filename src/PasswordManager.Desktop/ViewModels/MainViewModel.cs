using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Application.Abstractions;
using PasswordManager.Desktop.Services;

namespace PasswordManager.Desktop.ViewModels;

/// <summary>Shell state for Phase 1: theme, language, and section selection. No vault state yet.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settings;
    private readonly ILocalizer _localizer;
    private readonly ThemeService _theme;

    public MainViewModel(IAppSettingsStore settings, ILocalizer localizer, ThemeService theme)
    {
        _settings = settings;
        _localizer = localizer;
        _theme = theme;
        _isDarkTheme = theme.Current == "Dark";
        _shellFlowDirection = localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        localizer.CultureChanged += OnCultureChanged;
    }

    /// <summary>fa renders right-to-left; en mirrors the shell to left-to-right.</summary>
    [ObservableProperty]
    private FlowDirection _shellFlowDirection;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _selectedSection = "AllItems";

    /// <summary>Shown inside the language toggle: the language you would switch *to*.</summary>
    public string LanguageButtonLabel => _localizer.CurrentCulture.TwoLetterISOLanguageName == "fa" ? "EN" : "فا";

    /// <summary>Shown inside the theme toggle: the theme you would switch *to*.</summary>
    public string ThemeGlyph => IsDarkTheme ? "☀" : "☾";

    partial void OnIsDarkThemeChanged(bool value) => OnPropertyChanged(nameof(ThemeGlyph));

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _theme.Toggle();
        _settings.Update(_settings.Current with { Theme = next });
        IsDarkTheme = next == "Dark";
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = _localizer.CurrentCulture.TwoLetterISOLanguageName == "fa" ? "en" : "fa";
        _localizer.SetCulture(next);
        _settings.Update(_settings.Current with { Language = next });
        OnPropertyChanged(nameof(LanguageButtonLabel));
    }

    /// <summary>Placeholder until the vault lifecycle exists (Phase 3).</summary>
    [RelayCommand]
    private void Lock()
    {
    }

    /// <summary>Placeholder section selection until real navigation lands with feature phases.</summary>
    [RelayCommand]
    private void SelectSection(string section)
    {
        SelectedSection = section;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        ShellFlowDirection = _localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        OnPropertyChanged(nameof(LanguageButtonLabel));
    }
}
