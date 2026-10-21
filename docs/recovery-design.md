# Recovery Design — Ganjineh Recovery Key

Status: **Phase 0 — Proposed** | Implements FR-RC-*; crypto per cryptographic-specification.md §3, §6.

## 1. Principle

Recovery is a *second, independent root of trust for the vault*: a high-entropy random secret
the user stores offline. It must work with no server, no master password knowledge, no security
questions, and no hints. It is deliberately **as powerful as the master password** — the UI says
so, in Persian first.

## 2. Recovery Key Format

- `RecoverySecret`: 32 bytes (256 bits) from CSPRNG.
- Encoding: Crockford Base32 (I/L→1, O→0, U excluded), of the 32-byte secret (52 chars) plus a
  4-byte CRC-32 of the secret (7 chars, catches typos), dash-grouped in fours after the prefix:

```
PMRK1-ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789-ABCD-EFGH-JKLM-NPQR-STU7   (6 + 59 + separators)
```

- Parsing: strip dashes/spaces, uppercase, map Crockford homoglyphs, validate version prefix
  `PMRK1`, validate checksum → single field-level error message before any KDF work (cheap
  rejection prevents using the KDF as an oracle for "is this key structurally plausible").
- Case: entry is case-insensitive; display is uppercase.

## 3. Cryptographic Role

`RKEK = HKDF(RecoverySecret, salt=VaultID, info="ganjineh.v1|recovery-kek")`; the recovery wrap
record in the header contains `AES-GCM(RKEK, VMK, aad=HEADER_AAD)`. No Argon2 on this path: the
input already carries full entropy, so recovery unlock is near-instant once entered.

## 4. Workflows

### 4.1 Setup (vault creation, mandatory)
1. Generate secret → encode → display full-screen with copy button and "save to file" option.
2. Explicit confirmation step: user re-enters 4 random challenge groups from the key
   (proves it was transcribed/saved) — not a full retype of 59 chars.
3. Only after confirmation is the wrap written and the vault created. Plaintext secret never
   persisted; display buffer zeroized on screen close. **Never** logged, never in crash data.

### 4.2 Unlock with Recovery Key
Vault picker → "Unlock with Recovery Key" link → parse/validate → RKEK → unwrap VMK → normal
unlock path (key-management.md §5 steps 4–7).

### 4.3 Reset forgotten master password
1. Unlock via 4.2 (vault opens normally under VMK).
2. "Change master password" flow runs with elevated wording: new MP + new wrap written atomically.
3. Old password wrap removed; recovery wrap unchanged. VMK and all data unchanged.

### 4.4 Rotation (FR-RC-4)
1. Settings → Recovery → "Regenerate Recovery Key" → confirm master password (proves operator
   control) → new secret generated + shown + confirmed (4.1).
2. New wrap replaces the old recovery wrap in a single atomic header write; old wrap record
   revoked (deleted). Unlocks with the old key fail from that point **on that vault copy**.
3. Data untouched; other wraps untouched.

### 4.5 Failure handling
| Case | Behavior |
|------|----------|
| Bad prefix/checksum | Inline field error; no KDF run |
| Valid format, wrong key | Generic `AuthenticationFailed` (indistinguishable from tampering) |
| Repeated failures | No lockout (offline; lockout adds no security vs. offline copy) but rate limiting of UI attempts and full-screen warning after 5 |
| Header has no recovery wrap (should not exist; all v1 vaults have one) | Recovery option disabled with explanatory state |

## 5. Threat Notes (surfaced in UI and docs, per contract §4)

- **Holders can enter.** Anyone with the Recovery Key + the vault file can open the vault, copy
  it, or reset its master password. Share the key with no one; store it offline (printed or in a
  different strong location).
- **Revocation is forward-only.** Rotation revokes the old *wrap record in the live vault*, but
  cannot reach copies an attacker already made. An attacker holding an *older vault copy* and
  the Recovery Key *from that era* can still open that older copy (threat-model.md R-2, R-3).
  The product must never imply otherwise.
- **Losing both MP and Recovery Key = permanent loss.** There is no third path; this is stated
  during setup and in documentation. No fake recovery exists.

## 6. Test Obligations

| Requirement | Test |
|-------------|------|
| Setup confirmation gates vault creation | ST-UI-2 |
| Unlock success + failure paths | ST-SEC-2 |
| Rotation revokes old wrap (old key fails, new succeeds, data intact) | ST-SEC-2b |
| Reset MP keeps VMK/data; old MP fails afterward | ST-SEC-2c |
| Key/checksum parsing (homoglyphs, case, spacing) | ST-KAT-6 |
| Secret never appears in logs/UI dumps | ST-SEC-9 |
