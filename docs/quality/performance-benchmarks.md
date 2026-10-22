# Performance Benchmarks — Phase 8

Date: 2026-10-22 | Machine: dev-class x64, .NET 10.0.11 runtime, Windows 11 Enterprise 22621

| Operation | Measurement | Method |
|-----------|------------|--------|
| Argon2id (128 MiB, t=3, p=1) | median 172 ms, p95 183 ms | 20 samples, Stopwatch, warm |
| Vault unlock (Argon2id + header + DB + keyring) | ~250 ms | End-to-end (IntegrationTests timing) |
| Search 0 items | < 1 ms | Trivial |
| List 1 login item | < 5 ms | Integration test |
| Backup create (0 items, 0 attachments) | < 100 ms | Integration test |
| Backup verify (Cryptographic) | < 50 ms | Integration test |
| Backup verify (Rehearsal) | ~250 ms | Includes VACUUM INTO extract + DB open + integrity_check |
| Full restore (0 items) | ~250 ms | Includes Argon2id + DB validation + lifecycle unlock |

## Budgets

NFR-1 (unlock ≤ 2 s p95): **PASS** at 250 ms on dev hardware; min-spec projection ~0.5–1 s.
NFR-2 (search ≤ 100 ms at 5 k items): not benchmarked at scale (only 0–1 item tests) — Phase 8 gap.

## Limitations

- Measurements are from a single dev machine, not a statistically representative sample.
- ARM64 performance not measured (no ARM64 test environment).
- Large-vault scaling (> 10 000 items) not benchmarked; the in-memory index and
  per-item envelope decryption may degrade. Documented as a future optimization area.
