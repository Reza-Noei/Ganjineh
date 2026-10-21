# Database Design — SQLCipher Schema

Status: **Phase 0 — Proposed** | Format v1; crypto conventions per cryptographic-specification.md.

## 1. Connection and Cipher Configuration

One SQLCipher database per vault (`vault.db`). Connection is opened only by `VaultSession`;
never pooled across vaults; closed and forgotten at lock/switch.

```sql
PRAGMA key = "x'<64 hex of db_key>'";   -- raw key, bypasses SQLCipher's PBKDF2 (crypto-spec §4)
PRAGMA cipher_page_size = 4096;
PRAGMA kdf_iter = 0;
PRAGMA cipher_memory_security = ON;
PRAGMA journal_mode = WAL;              -- WAL/-shm are cipher-encrypted by SQLCipher
PRAGMA synchronous = FULL;              -- durability over speed; vault writes are rare
PRAGMA temp_store = MEMORY;
PRAGMA foreign_keys = ON;
```

Startup assertions per open: `cipher_version` non-empty; `PRAGMA integrity_check` returns `ok`;
foreign-key check clean. Failure ⇒ vault marked "needs attention" with restore guidance, never
silent (FR-BK-6). Data access via ADO.NET over the SQLCipher provider (ADR-002); EF Core is
*not* used — the schema is small and fixed, and hand-written SQL keeps the encryption-critical
surface auditable. (Rationale recorded here rather than a full ADR; revisit if item search
complexity grows.)

## 2. General Conventions

- All timestamps: Unix seconds, `INTEGER`.
- All sensitive text/payload columns are `BLOB` envelopes (crypto-spec §7) produced/verified by
  the Application layer — the DB layer never sees plaintext secrets, so SQLCipher is the second,
  not first, line of defense.
- Structural columns (types, IDs, relations, timestamps, trashed/favorite flags) are plaintext:
  needed for list queries before per-row decryption and judged non-sensitive (header-level
  rationale: vault-file-format.md §3).

## 3. Schema (v1)

```sql
CREATE TABLE schema_migrations (
  version     INTEGER PRIMARY KEY,
  applied_at  INTEGER NOT NULL
);

CREATE TABLE vault_meta (               -- exactly one row, id = 1
  id            INTEGER PRIMARY KEY CHECK (id = 1),
  name_cipher   BLOB NOT NULL,          -- envelope, purpose "meta.name"
  settings_cipher BLOB NOT NULL,        -- envelope, purpose "meta.settings" (clipboard TTL,
                                        -- autolock minutes, theme, backup dir + retention, ...)
  created_at    INTEGER NOT NULL,
  updated_at    INTEGER NOT NULL
);

CREATE TABLE folders (
  id          INTEGER PRIMARY KEY,
  parent_id   INTEGER NULL REFERENCES folders(id),   -- one nesting level in v1 (enforced in app)
  name_cipher BLOB NOT NULL,            -- envelope, purpose "folder.name:<id>"
  sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE tags (
  id          INTEGER PRIMARY KEY,
  name_cipher BLOB NOT NULL,            -- envelope, purpose "tag.name:<id>"
  color       TEXT NOT NULL             -- token name from theme palette; non-sensitive
);

CREATE TABLE items (
  id          TEXT PRIMARY KEY,         -- UUID string
  type        INTEGER NOT NULL,         -- 1 login, 2 note, 3 card, 4 identity,
                                        -- 5 wifi, 6 api, 7 license, 8 recovery-codes
  favorite    INTEGER NOT NULL DEFAULT 0,
  trashed_at  INTEGER NULL,             -- NULL = live; value = trash timestamp
  created_at  INTEGER NOT NULL,
  updated_at  INTEGER NOT NULL
);
CREATE INDEX ix_items_type   ON items(type)     WHERE trashed_at IS NULL;
CREATE INDEX ix_items_trash  ON items(trashed_at);

CREATE TABLE item_versions (            -- append-only history; current = max(version)
  item_id      TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
  version      INTEGER NOT NULL,
  payload_cipher BLOB NOT NULL,         -- envelope, purpose "payload.v1", record_id = item UUID
  created_at   INTEGER NOT NULL,
  PRIMARY KEY (item_id, version)
);

CREATE TABLE item_tags (
  item_id  TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
  tag_id   INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
  PRIMARY KEY (item_id, tag_id)
);

CREATE TABLE attachments (
  id            TEXT PRIMARY KEY,       -- UUID
  item_id       TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
  filename_cipher BLOB NOT NULL,        -- envelope, purpose "att.filename:<id>"
  size_bytes    INTEGER NOT NULL,       -- plaintext: needed for quota/space UI
  filekey_cipher BLOB NOT NULL,         -- sealed file_key envelope (crypto-spec §8)
  created_at    INTEGER NOT NULL
);
CREATE INDEX ix_att_item ON attachments(item_id);

CREATE TABLE totp_config (              -- 0..1 rows per login item; inside payload for export
  item_id     TEXT PRIMARY KEY REFERENCES items(id) ON DELETE CASCADE,
  config_cipher BLOB NOT NULL          -- envelope: secret, algo, digits, period, issuer, label
);
```

TOTP secrets live in the login payload *and* `totp_config` only when configured; both are
envelopes, kept in sync transactionally.

## 4. Search

- **No persistent search index** (contract: no plaintext indexes). At unlock, the Application
  layer decrypts all live payloads once (NFR-2 budget) and builds an in-memory index
  (normalized tokens per item, incl. Persian-aware folding: Arabic Yeh/Kaf normalization,
  ZWNJ handling, diacritic stripping, case folding for Latin).
- Queries hit the in-memory index; tag/folder/type filters hit SQL; results join in memory.
- On lock, the index and all decrypted payloads are discarded (zeroized where buffers allow).

## 5. Transaction and Integrity Rules

- Every user-visible mutation (item save, folder rename, tag ops, attachment registration) is a
  single SQL transaction; envelope encryption happens *before* `BEGIN` so no key work occurs
  inside write locks.
- WAL checkpoint (`TRUNCATE`) runs on lock and after backup, so `.gvbak` copies never straddle
  WAL state.
- History: saving an item appends `item_versions` (cap per item configurable, default 50,
  oldest pruned on save — pruning is part of the same transaction).

## 6. Migrations

- Forward-only, numbered by `schema_migrations`; each migration runs on a **copy** of `vault.db`
  (`vault.db.migrating`) inside one transaction, then atomic rename swap; failure deletes the
  copy and leaves the original byte-identical (ST-INT-9).
- App refuses to run a migration when the vault's last backup is missing or stale
  (>24 h) unless the user explicitly overrides — migrations are the one operation that rewrites
  every page.

## 7. Backups (`.gvbak` v1)

```
[0x00] magic "GPBAK1\0"
[8]    u32 manifest_len;  [12] manifest_json (UTF-8)     -- manifest is part of AAD
       manifest: { vaultId, formatVersion, schemaVersion, createdAtUnix,
                   innerSha256, itemCount, appVersion, backupId (uuid) }
[..]   wrap: nonce(12) + AES-GCM(backup_wrap_key, backup_key) + tag
[..]   body: nonce(12) + AES-GCM(backup_key, inner bytes) + tag
       inner bytes = "vault.db" checkpointed image (SQLCipher file as-is)
```

- Created via `VACUUM INTO` to a temp file (consistent snapshot) → read bytes → envelope →
  write `.gvbak.tmp` → flush → replace. Restore validates magic/manifest AAD/both tags, then
  SHA-256 of decrypted bytes, then opens the inner DB and runs `integrity_check` — only then
  creates the new vault directory.
- Retention: user-configured N (default 5) per vault; oldest pruned after successful new backup.

## 8. At-Rest Verification Obligations

| Check | Test |
|-------|------|
| `vault.db` unreadable by plain SQLite (no key) | ST-DB-1 |
| WAL/`-shm`/temp files contain no plaintext item text (byte scan for known secrets) | ST-DB-2 |
| `cipher_version` check gates open | ST-DB-3 |
| Backup tamper (flip tag byte / truncate / swap old body) ⇒ refuse | ST-SEC-4 |
| Interrupted write simulation (kill after BEGIN / during replace) ⇒ vault still opens | ST-INT-7 |
