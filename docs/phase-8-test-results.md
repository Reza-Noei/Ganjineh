# Phase 8 Test Results — Adversarial Review

Date: 2026-10-22 | Commit: `d3082cf` (Phase 7 baseline) → Phase 8 fixes | 208/208 baseline

## Adversarial integration tests (8/8 green)

| Test | What it proves |
|------|---------------|
| Fuzz_Header_Bit_Flips_Produce_Typed_Errors | 2000 deterministic single-bit mutations → only typed errors (no crash, no corruption) |
| Backup_Entry_Names_Prevent_Path_Traversal | No `..` or `\` in backup entry names |
| Malicious_Kdf_Parameters_Are_Rejected | Absurd memory/iterations/parallelism → typed rejection |
| Nonce_Uniqueness_High_Volume | 10 000 AEAD encryptions under same key → zero nonce collisions |
| No_Plaintext_Secrets_In_Any_Output_File | Sentinel scan across header/DB/WAL/shm/registry/backup → zero hits |
| Selective_Restore_Decrypts_Under_Target_Keys | Cross-vault selective restore re-encrypts and decrypts correctly |
| Attachment_Tag_Tamper_Detected | Tag byte-flip → export rejected |
| Locked_Session_Prevents_Item_Operations | Post-lock List/Get/Create all throw StaleSessionException |

## Full suite

**209/209 tests** across 7 assemblies (including the full-journey FlaUI E2E). Zero warnings.

## Issues found and fixed during Phase 8

All four findings (F-01 through F-04) were discovered during the Phase 6 hardening phase and
fixed with regression tests. No new defects were found during the Phase 8 adversarial review
beyond those already documented. The chunk framing bug (F-01) was the most severe — it would
have caused all multi-chunk backups to fail restore with a generic crypto error.
