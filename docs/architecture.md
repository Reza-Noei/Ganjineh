# Architecture — Ganjineh

Status: **Phase 0 — Proposed** | Clean Architecture + MVVM, modular monolith, single process.

## 1. Solution Layout

```
PasswordManager.sln
├── src/
│   ├── PasswordManager.Domain/          entities, value objects, enums, domain errors, result types
│   ├── PasswordManager.Application/     use cases, vault lifecycle, item CRUD, search, generator,
│   │                                    totp, security center, import/export orchestration
│   ├── PasswordManager.Cryptography/    KDF, HKDF, AEAD envelopes, keyring, recovery codec
│   │                                    (depends ONLY on Bcl + NSec; no WPF, no DB, no IO)
│   ├── PasswordManager.Persistence/     SQLCipher access, header read/write, vault registry,
│   │                                    backup file IO, atomic file operations
│   ├── PasswordManager.Infrastructure/  clipboard, Windows session events, localization, logging,
│   │                                    settings storage, theming hooks, dialogs
│   └── PasswordManager.Desktop/         WPF shell, views, view-models, DI composition root
└── tests/
    ├── Domain.Tests/  Application.Tests/  Cryptography.Tests/  Persistence.Tests/
    ├── IntegrationTests/                 vault lifecycle end-to-end on real files
    ├── SecurityTests/                    KATs, tamper, isolation, at-rest (owns ST-SEC/ST-DB)
    └── Desktop.UITests/                  FlaUI (UIA3) scenarios
```

**Dependency rule:** `Domain` ← `Application` ← {`Persistence`, `Infrastructure`} → `Desktop`;
`Cryptography` is referenced by `Application` and `Persistence` and references nothing internal.
`Domain` references no framework package at all (no WPF, no EF, no SQLCipher, no JSON
attributes). Interfaces the Application layer needs (clipboard, clock, file dialogs, session
events) are defined **in Application** and implemented in Infrastructure — the only inversion
points; no generic repositories, no CQRS machinery, no unit-of-work abstractions (contract §9:
concrete needs only).

## 2. Runtime Composition

`Desktop` is the composition root (`Microsoft.Extensions.Hosting`):

- `IHostedService`-free simple singleton graph; services registered explicitly (no assembly
  scanning magic — auditable wiring).
- Lifetime model: `VaultManager` (singleton) → `VaultSession` (one per unlocked vault; owns
  keyring, DB connection, in-memory index, undo-free command state). Only **one** vault session
  may be active in the UI at a time; background sessions are disallowed in v1 (simplicity +
  smaller key-material footprint).
- Dispatch: all use cases are `async`; UI updates marshal via `Dispatcher`; no `async void`
  outside command wrappers that catch and route to the error pipeline.

## 3. Key Service Contracts (shape, not full signatures)

```csharp
// Application
interface IVaultLifecycle {
    Task<VaultHandle> CreateAsync(CreateVaultSpec spec, CancellationToken ct);   // returns handle + recovery key (once)
    Task<VaultHandle> UnlockAsync(VaultRef vault, VaultCredential credential, CancellationToken ct);
    Task LockAsync(VaultHandle vault);          // zeroize + close + clear UI state
    Task ChangeMasterPasswordAsync(VaultHandle vault, PasswordChange change, CancellationToken ct);
    Task RotateRecoveryKeyAsync(VaultHandle vault, out RecoveryKeyPlaintext newKey, CancellationToken ct);
    Task DeleteVaultAsync(VaultRef vault, ConfirmationToken confirm, CancellationToken ct);
}
interface IItemRepository { /* typed CRUD incl. history, trash; all envelopes handled above */ }
interface ISearchService { IReadOnlyList<ItemSummary> Search(SearchQuery q); }
interface IPasswordGenerator { string Generate(GeneratorOptions o); Passphrase GeneratePassphrase(PassphraseOptions o); }
interface ITotpService { TotpCode Now(TotpConfig c); }
interface ISecurityAudit { SecurityReport Analyze(IReadOnlyList<DecryptedItem> items); }
interface IBackupService { Task<BackupResult> BackupAsync(VaultHandle v, BackupOptions o, CancellationToken ct); ... }
interface IImporter { ImportPreview Preview(ImportSource src); ImportResult Apply(ImportPreview p); }

// Infrastructure implementations (interfaces owned by Application)
IClipboardService   // copy-with-expiry + ownership-safe clear
IWindowsSessionEvents // SessionSwitch → autolock
ILocalizer, IAppSettings, IFileSystemDialogs, IAppLogger
```

Errors: `Result<T, AppError>` (no exceptions across the Application boundary); typed
`AppError` taxonomy (`AuthenticationFailed`, `FormatError`, `StorageError`, `PolicyViolation`,
`Cancelled`) — UI maps to Persian messages; `AuthenticationFailed` is never differentiated.

## 4. Secrets Handling Rules (cross-cutting)

- `VaultCredential` (master password / recovery key) lives in cleared `char[]`/`byte[]` from the
  password-entry control to the KDF; WPF `PasswordBox` (not bound to DP strings) via an
  attached behavior that hands over the buffer and zeroes it.
- Decrypted payloads are materialized only into view-models for the visible page ± neighbors;
  scrolling lists render *metadata summaries*; secret fields fill on selection and clear on
  navigation away.
- Logging is allowlist-structured (`Serilog` → rolling file, no sinks beyond file); a compile-time
  analyzer forbids passing `SecretBuffer`/`char[]` into log calls; exception filters strip
  crypto-layer frames' data. Crash dumps: disabled (NFR-5).

## 5. UI Layer Conventions (see ui-ux-specification.md for the design itself)

- MVVM via `CommunityToolkit.Mvvm` (`ObservableObject`, `[RelayCommand]`, `[ObservableProperty]`,
  messengers limited to navigation + lock events).
- One view-model per screen; item list virtualization (`VirtualizingStackPanel`); no code-behind
  beyond attached behaviors.
- Navigation: single `MainWindow` hosting the three-column shell + overlay pages (onboarding,
  unlock) as `ContentControl` states — no hidden windows holding stale secret state.
- Localization: `resx`, neutral culture = fa, `en` satellite; `x:Static`-free dynamic lookup;
  `FlowDirection` at shell level; per-control bidirectional overrides where Persian text embeds
  Latin identifiers.

## 6. Persistence Layer Internals

- `HeaderCodec` (parse/serialize header.bin, strict rules of vault-file-format.md §5);
  `VaultRegistry` (vaults.json); `SqlCipherConnectionFactory`; `EnvelopeCodec` lives in
  Cryptography; Persistence composes them. All file writes go through `AtomicFile` helper
  (temp → flush-to-disk → replace) — the only sanctioned write path.

## 7. Future Extension Seams (designed, not implemented)

- `VaultSession` exposes an internal `ISessionKeyProvider` (internal, not public) so a future
  browser-extension native-messaging host could request *field-scoped* access without re-plumbing
  the keyring — deliberately `internal` to prevent accidental surface in v1.
- Import pipeline is plugin-shaped (`IImporter` implementations registered in DI) for future
  formats; no plugin loading from disk in v1.
- No interfaces exist for sync/cloud/passkeys (contract §10: no unused speculative abstractions).
