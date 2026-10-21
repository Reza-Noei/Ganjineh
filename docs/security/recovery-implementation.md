# Recovery Implementation — as built (Phase 2)

## Format (implemented in `RecoveryKeyCodec`)

- Secret: 32 bytes (256 bits) from the OS CSPRNG.
- Wire/display form: `PMRK1-` + Crockford-Base32(secret, 52 chars) + Base32(CRC-32(secret), 7
  chars), dash-grouped every 4 characters after the prefix (79 characters total).
- Checksum: standard CRC-32 (IEEE) over the raw secret — a *transcription* checksum, not a
  cryptographic primitive (KAT: "123456789" → 0xCBF43926).
- Parsing: strips dashes/spaces, uppercases, maps homoglyphs (I/L→1, O→0, U→V), validates the
  prefix, exact length, padding bits, and checksum **before any cryptographic work** — cheap
  rejection prevents using the KDF as a structure oracle.

## Workflows (implemented in `VaultLifecycleService`, proven by tests)

| Workflow | Behavior | Test |
|----------|----------|------|
| Setup | Recovery secret generated during vault creation; returned exactly once in `CreatedVault` (caller displays, then disposes) | Create_Produces_Unlocked_Session_And_Displayable_Recovery_Key |
| Unlock | RKEK = HKDF(secret, VaultID); unwrap recovery wrap; normal unlock path | Recovery_Key_Unlocks_Without_Master_Password |
| Reset master password | Recovery-authenticated unlock → fresh salt + new password wrap, atomic header write; recovery wrap preserved byte-for-byte; old password fails afterward | Reset_Forgotten_Master_Password_With_Recovery |
| Rotation | New secret/wrap replaces the old wrap in one atomic header write; old key fails on the live vault; master password and data untouched | Recovery_Rotation_Revokes_Old_Key_Keeps_Data_And_Password |
| Invalid input | Bad format ⇒ `CryptoFormatException`; wrong key ⇒ generic `CryptoAuthenticationException` (no stage disclosure) | Invalid_Recovery_Key_Is_Rejected, SecretHygieneTests |

## Security Notes (surfaced in product docs later; normative here)

- Recovery unlock needs no Argon2id: the secret carries full entropy, so recovery is instant.
- **Forward-only revocation**: rotation deletes the old wrap from the live vault; an attacker
  holding an older vault snapshot *plus* the recovery key from that era can still open that older
  copy (threat-model R-3). The product must never claim otherwise.
- The plaintext Recovery Key is never persisted, logged, or included in exceptions
  (ST-SEC-9 verified: sentinel scans over logs/files; message hygiene tests).
- No security questions, no hints, no third path: losing both MP and Recovery Key = permanent
  data loss.
