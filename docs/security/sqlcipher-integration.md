# SQLCipher Integration — as built (Phase 2)

## Provider Stack (ADR-002 resolved)

| Layer | Package | Version |
|-------|---------|---------|
| Native engine | SQLitePCLRaw.lib.e_sqlcipher (SQLCipher 4.x community) | 2.1.11 |
| Provider | SQLitePCLRaw.provider.e_sqlcipher | 2.1.11 |
| Binding | SQLitePCLRaw.core | 2.1.11 |

**Microsoft.Data.Sqlite was rejected after the Phase 2 spike**: it transitively bundles the plain
`e_sqlite3` engine whose battery initializer races the SQLCipher bundle — on this machine
`PRAGMA cipher_version` came back **empty** (unencrypted engine silently selected). The
implementation therefore uses raw SQLitePCL with one explicit, testable initialization:

```csharp
raw.SetProvider(new SQLite3Provider_e_sqlcipher());   // once per process, before any connection
```

## Keying and Session Pragmas (`SqlCipherConnection.OpenOrCreate`)

```sql
PRAGMA key = "x'<64 hex chars of db_key>'";  -- raw key: bypasses SQLCipher's internal PBKDF2
PRAGMA cipher_page_size = 4096;
PRAGMA kdf_iter = 0;
PRAGMA cipher_memory_security = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA temp_store = MEMORY;
PRAGMA foreign_keys = ON;
```

`db_key` = `HKDF(VMK, salt=VaultID, "sqlcipher-key")` — Argon2id already ran for the password
path, so the DB open adds no KDF cost. WAL and `-shm` files are cipher-encrypted by SQLCipher;
`temp_store = MEMORY` keeps spill files out of disk entirely.

## Genuineness Gates (never trust the package name — ADR-002)

Enforced at every open, verified by tests:
1. `PRAGMA cipher_version;` must be non-empty ⇒ otherwise `CipherProviderException`, vault refused.
2. Correct key: sentinel round-trip through a real table (spike + lifecycle tests).
3. Wrong key: page authentication fails — statement execution returns an error (never rows).
4. Unkeyed open: cannot read `app_meta` (SecurityTests.Database_Cannot_Be_Opened_Without_The_Key).
5. File at rest: does not start with `SQLite format 3` (SecurityTests).
6. Identity: `app_meta.vault_id` (encrypted) must equal the header's authenticated VaultID ⇒
   `VaultIdentityMismatchException` otherwise (cross-attachment detection).

## Crash Behavior and Temporary Files

- `synchronous = FULL` + WAL gives atomic commits; interrupted writes recover on reopen
  (lifecycle crash tests exercise header-side faults; DB-side WAL recovery is SQLite's).
- `temp_store = MEMORY`: SQLite temporary tables/sorters never touch disk inside the vault.
  **Limitation:** .NET's own managed temporary files (e.g., `Path.GetTempFileName` use) are NOT
  covered by SQLCipher — Phase 2 code creates none; future phases must route any temp need
  through encrypted paths and this rule is on the release checklist.
- `CheckpointWal()` (TRUNCATE) runs on lock, so `.gvbak` copies (Phase 6) never straddle WAL state.

## Schema (v1, minimal by design — full CRUD is Phase 3)

```sql
CREATE TABLE app_meta (key TEXT PRIMARY KEY, value BLOB NOT NULL);
-- rows: vault_id (16 B), schema_version, format_version, created_at_unix
```
