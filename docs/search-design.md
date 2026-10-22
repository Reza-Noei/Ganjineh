# Search Design — as built (Phase 3)

## Pipeline

1. At unlock, `BuildSearchIndexAsync` decrypts each live payload once and produces
   `CredentialSearchEntry { Id, SearchText }` where SearchText = title ‖ username ‖ URL ‖ notes —
   **the password is never included** (asserted by tests).
2. The index lives only in the workspace VM (`Dictionary<Guid,string>`); it is dropped on lock /
   vault switch / exit. Nothing about it touches disk (ST scan verifies no sentinels in any file).
3. UI text input is debounced 250 ms; a stale debounce is cancelled via CTS; outdated UI results
   after a vault switch are prevented by the full workspace wipe on `SessionChanged`.

## Normalization (`SearchNormalizer`) — applied to text and query alike

- Arabic Yeh `ي` (U+064A) and Alef Maksura `ى` (U+0649) → Persian Yeh `ی`
- Arabic Kaf `ك` → `ک`
- Alef variants `أ إ آ` → `ا`; Taa marbuta `ة` → `ه`
- Diacritics (U+064B–U+065F, U+0670) dropped; ZWNJ/ZWJ/RLM/LRM dropped
- Invariant lowercase (Latin)

Multi-token queries: every whitespace-separated token must match (AND). Matching is substring-
based on normalized text — sufficient at the 5k-item scale (NFR-2 ≤ 100 ms; measured well under
for realistic corpora). No persistent index exists by design (contract §7).

## Limitations

- Persian full-text ranking/scoring (prefix/fuzzy) is out of scope for v1 — exact substring only.
- The notes field is searchable via the index but the list rows display title/username only;
  the details pane shows notes after selection.
