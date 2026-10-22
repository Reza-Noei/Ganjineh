# Advanced Search — as built (Phase 4)

## Index

At unlock (and after each mutation), `BuildSearchIndexAsync` produces one in-memory entry per
live item: title + description + allowed Data keys (per-kind allowlist) + non-concealed custom
field labels/values + decrypted folder name + decrypted tag names. The password, concealed
custom fields and card/API secrets are structurally excluded (allowlist, tested).

- Persian normalization identical to Phase 3 (ي/ى→ی, ك→ک, alef variants, taa marbuta,
  diacritics, ZWNJ/marks, invariant lowercase); multi-token AND matching.
- The index lives only in the workspace VM; wiped on lock/switch/exit (tested via sentinel scans
  and the session-boundary wipe).

## Filters and sorting

`ItemFilter` supports kind, folder, tag(s), favorites-only, trashed-only, updated-after/before
and sort key (`updated` default, `title`, `created`). The UI composes filters from the nav
section + folder/tag panels + favorites toggle. Combined filters are AND-ed.

## Memory tradeoff

The in-memory index holds decrypted searchable text for the whole vault (~hundreds of bytes per
item + custom fields). At the 5 000-item scale this is a few MB — acceptable for a desktop
process and far cheaper than re-decrypting per keystroke. Very large vaults (10⁵ items) would
warrant a per-page index; documented as a future optimization, not needed for v1.

## Exclusions

Trashed items never enter the live index (a separate trashed-only index can be built
explicitly); locked vaults expose no index at all; nothing is persisted.
