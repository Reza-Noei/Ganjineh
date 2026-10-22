# Phase 3 Implementation — Multi-Vault Management, Credential CRUD & Persian WPF

Status: Implemented and tested (see phase-3-test-results.md) | 2026-10-22

## What was built

1. **Vault management UI + services**: startup vault picker (create / open-existing / remove-from-list /
   delete-with-confirmation), guided creation (name → location → password + confirm + strength hint →
   Recovery Key reveal with transcription challenge), unlock (password and recovery), manual lock,
   auto-lock (idle + Windows session lock), switch, rename, delete. At most one unlocked vault at a
   time (`IVaultSessionManager`); switching locks the previous session.
2. **Login credentials**: create / list / detail / update / delete with validation, Unicode-safe,
   encrypted at two layers (SQLCipher + per-item AEAD envelopes). Passwords decrypted only for
   reveal / copy / edit flows; list rows never carry password material.
3. **Search**: debounced (250 ms) in-memory search over title / username / URL / notes with
   Persian normalization (ي→ی, ى→ی, ك→ک, alef variants, taa marbuta, diacritics, ZWNJ/direction
   marks). The search index is built once per unlock in memory — **never persisted**; the
   password is excluded from the index by construction.
4. **Clipboard**: copy username/password with ownership-safe expiration (default 30 s per the
   Phase 3 contract; 10–120 s configurable). Clears only if the clipboard still holds exactly what
   we copied. Status-bar feedback; clipboard-history warning documented (clipboard-security.md).
5. **Persian WPF**: three-column RTL workspace (nav right / list center / details left), Fluent-
   inspired tokens from Phase 1, Vazirmatn, Light/Dark, localized fa/en with live switching and
   FlowDirection mirroring, keyboard navigation, visible automation ids for UIA tests.

## Architectural decisions (this phase)

- **Vault display name** lives encrypted inside the vault DB (`app_meta.vault_name`, envelope under
  `item_key`, AAD-bound to the VaultID) and is authoritative; the registry additionally carries an
  optional plain-text **local label** explicitly marked this-device-only (shown pre-unlock).
  Rename writes both; cryptographic identity never changes.
- **Key-free persistence**: `ItemRepository` stores only sealed envelopes; encryption happens in
  the Application layer (`ItemEncryption`) with the session's `item_key`. Persistence never sees
  keys or plaintext (architecture.md dependency rule).
- **Item schema is type-agnostic** (`items` + append-only `item_versions`), so Phase 4 item types
  need no migration — only new type ids and payload shapes.
- **Passwords and PasswordBox**: sensitive entry flows through code-behind handoffs into
  `char[]` buffers (never bound to observable string properties). Pragmatic MVVM deviation,
  documented in session-lifecycle.md §password-handling.
- **Unimplemented sections render as disabled controls with a "later" tooltip** — no fake buttons.

## Files (summary — see git log for the full list)

- Persistence: `Items/ItemRepository.cs`, `SqlCipher/SqlCipherConnection.cs` (+Query), `SqlCipher/VaultDatabase.cs` (encrypted meta), `Registry/VaultRegistry.cs` (labels/list/remove)
- Application: `Vaults/VaultCatalog.cs`, `Vaults/VaultSessionManager.cs`, `Credentials/*` (models, ItemEncryption, CredentialService, SearchNormalizer), `Abstractions/IDialogService.cs`, settings extensions, `RenameVaultAsync`/`GetVaultName` on the lifecycle
- Desktop: `ViewModels/` (AppViewModel, VaultPickerViewModel, CreateVaultViewModel, UnlockViewModel, VaultWorkspaceViewModel), `Services/` (ClipboardService, AutoLockService, DialogService), `Localization/Converters.cs`, `MainWindow.xaml(+cs)` full rewrite, resx additions (≈100 keys ×2 locales)
