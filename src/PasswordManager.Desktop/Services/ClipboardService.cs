using System.Runtime.InteropServices;
using System.Windows;
using PasswordManager.Application.Abstractions;

namespace PasswordManager.Desktop.Services;

/// <summary>
/// Clipboard copy with ownership-safe expiration: the delayed clear fires only if the clipboard
/// still holds exactly what we copied — newer user content is never overwritten. Windows
/// Clipboard History (Win+V) and third-party managers are OUTSIDE this control; the UI warns.
/// </summary>
public sealed class ClipboardService
{
    private readonly IAppSettingsStore _settings;

    public ClipboardService(IAppSettingsStore settings) => _settings = settings;

    public event EventHandler? ClipboardExpired;

    /// <summary>Returns false when the OS refused the write (e.g. clipboard locked by another process).</summary>
    public bool TryCopyWithExpiry(string value)
    {
        try
        {
            Clipboard.SetText(value);
            var ttl = Math.Clamp(_settings.Current.ClipboardTimeoutSeconds, 10, 120) * 1000;
            _ = ScheduleClearAsync(value, TimeSpan.FromMilliseconds(ttl));
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ExternalException)
        {
            return false;
        }
    }

    private async Task ScheduleClearAsync(string copied, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(true);
        try
        {
            if (Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), copied, StringComparison.Ordinal))
            {
                Clipboard.Clear();
                ClipboardExpired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ExternalException)
        {
            // Clipboard busy — the value simply stays; never log the copied content.
        }
    }
}
