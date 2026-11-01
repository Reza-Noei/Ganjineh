namespace PasswordManager.Application.Abstractions;

public enum ApplicationDistributionMode
{
    Portable = 0,
    Installer = 1,
}

/// <summary>
/// Distribution-mode abstraction (Phase 7 §2): mode changes default paths only — never the
/// security model, Vault format, or cryptography. ViewModels consume this via DI, never via
/// scattered Portable/Installer checks.
/// </summary>
public interface IApplicationDistributionContext
{
    ApplicationDistributionMode Mode { get; }
    string ApplicationDirectory { get; }
    string ConfigurationDirectory { get; }
    string DefaultVaultDirectory { get; }
    string DefaultBackupDirectory { get; }
}

/// <summary>
/// Resolves the distribution context per ADR-006: Portable when a Data directory exists beside
/// the executable, Installer otherwise. The mode is detected, never toggled at runtime.
/// </summary>
public sealed class ApplicationDistributionContext : IApplicationDistributionContext
{
    public ApplicationDistributionContext(
        ApplicationDistributionMode mode, string applicationDirectory,
        string configurationDirectory, string defaultVaultDirectory, string defaultBackupDirectory)
    {
        Mode = mode;
        ApplicationDirectory = applicationDirectory;
        ConfigurationDirectory = configurationDirectory;
        DefaultVaultDirectory = defaultVaultDirectory;
        DefaultBackupDirectory = defaultBackupDirectory;
    }

    public ApplicationDistributionMode Mode { get; }

    public string ApplicationDirectory { get; }

    public string ConfigurationDirectory { get; }

    public string DefaultVaultDirectory { get; }

    public string DefaultBackupDirectory { get; }

    public static ApplicationDistributionContext Resolve(string? baseDirectory = null, string? profileDirectory = null)
    {
        var appDir = baseDirectory ?? AppContext.BaseDirectory;
        var portableData = Path.Combine(appDir, "Data");
        if (Directory.Exists(portableData))
        {
            // Portable: everything under <exe>\Data; relative paths survive moving the directory.
            return new ApplicationDistributionContext(
                ApplicationDistributionMode.Portable,
                appDir,
                portableData,
                Path.Combine(portableData, "Vaults"),
                Path.Combine(portableData, "Backups"));
        }

        var profile = profileDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var config = Path.Combine(profile, "Ganjineh");
        return new ApplicationDistributionContext(
            ApplicationDistributionMode.Installer,
            appDir,
            config,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ganjineh", "Vaults"),
            Path.Combine(config, "Backups"));
    }
}
