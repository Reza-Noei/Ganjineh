# Final Threat Model — as verified (Phase 8)

Updates the Phase 0 threat model with implementation-verified properties.

## Verified resistances (evidence-based)

| Threat | Verification |
|--------|-------------|
| Vault file theft (offline attack) | SQLCipher encryption + 2000-case header fuzz + sentinel scan (no plaintext in vault.db/WAL/shm) |
| Backup theft | .gvbak: authenticated manifest + chunked AEAD; tamper/truncate detected; wrong key rejected |
| Vault metadata tampering | 2000-case bit-flip fuzz: all mutations produce typed parse errors or auth failures |
| Cross-vault item substitution | Envelope AAD binds VaultID + itemID; selective restore into wrong vault → KeyNotFoundException |
| Recovery Key + vault theft | Recovery wrap authenticates independently; master-password reset works on restored vault |
| Password change integrity | Old password fails, new succeeds, recovery intact, item data preserved |
| Chunk tampering (attachments/backup) | Per-chunk AEAD + terminator; byte-flip detected; no partial output |
| Stale session access | Session boundary wipe + generation counter; locked session → StaleSessionException |

## Partially mitigated

| Threat | Mitigation | Limitation |
|--------|-----------|-----------|
| Malware on unlocked workstation | Auto-lock, clipboard TTL, secure memory hygiene | GC cannot guarantee zeroization; pagefile/crash dumps may capture secrets |
| Clipboard retention | TTL + ownership check | Windows Clipboard History and 3rd-party managers retain data beyond TTL |
| Restore onto source location | Restore.DestinationExists refusal | Original vault preserved but the staged backup remains on disk until manually removed |

## Outside the security boundary

Kernel rootkits, DMA attacks, cold-boot/DRAM remanence, hardware implants, compromised OS,
malicious local administrator, side channels (cache/timing/power), legal/coercion.
