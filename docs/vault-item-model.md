# Vault Item Model — as built (Phase 4)

## Storage split

| Layer | Content | Protection |
|-------|---------|-----------|
| `items` row | id, type, favorite, trashed_at, folder_id, timestamps | structural, non-sensitive (inside SQLCipher) |
| `item_versions` rows | append-only payload envelopes | SQLCipher + AEAD (item_key, domain=Item, AAD=VaultID‖itemId‖"payload.v1") |
| `folders` / `tags` rows | name envelopes | SQLCipher + AEAD (item_key, domain=Meta, AAD=VaultID‖"folder:<id>" / "tag:<id>") |
| `attachments` rows | filename envelope, file-key envelope, size, timestamps | SQLCipher + AEAD |
| `attachments/<id>.pmblob` | chunked AEAD content | attach_key + per-chunk nonces (attachment-encryption.md) |

## Payload v1 (normative shape)

```json
{
  "V": 1,
  "Title": "…",
  "Description": null,
  "Fields": [ { "Id": "guid", "Label": "…", "Type": 2, "Value": "…", "Order": 0, "Concealed": true } ],
  "Data": { /* type-specific keys, see ItemKindInfo */ }
}
```

`ItemPayload.Parse` detects legacy Phase 3 shapes (`{"T","U","P","W","N"}`) and maps them to v1
Login payloads in memory. `VaultItemService` normalizes deserialized `Data` values (JsonElement →
string / List<string> / "true"/"false") so consumers never see serializer artifacts.

## Type-specific Data keys

| Kind | Keys | Searchable |
|------|------|-----------|
| Login | username, password, urls[] | username, urls |
| Secure Note | content, category | category |
| Payment Card | holder, number, expMonth, expYear, cvv, issuer | holder, issuer |
| Identity | fullName, dob, emails[], phones[], address, idNumbers | fullName, emails, phones, address, idNumbers |
| Wi-Fi | ssid, security, password, hidden | ssid, security |
| API Credential | service, apiKey, apiSecret, token, endpoint, environment, expiresAt | service, endpoint, environment |
| Software License | product, key, email, purchasedAt, expiresAt, vendorUrl | product, email, vendorUrl |
| Recovery Codes | service, codes[] | service |
| Custom | — (custom fields only) | — |

Secret keys (password, number, cvv, idNumbers, apiKey, apiSecret, token, key, codes) are excluded
from the search index by the `SearchableDataKeys` allowlist.

## Invariants

- Every item belongs to exactly one vault (enforced by the per-vault DB + AAD binding).
- Item IDs are UUIDs, stable across edits; history rows reference them with FK cascade.
- Timestamps are UTC Unix seconds; `updated_at` bumps on every mutation.
- Title is required (≤200 chars); custom-field values validate per type (≤5000 chars).
