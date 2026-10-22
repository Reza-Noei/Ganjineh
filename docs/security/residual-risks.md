# Residual Risks — Phase 8 Assessment

## Accepted risks (not fixable in application code)

| ID | Risk | Severity | Rationale |
|----|------|----------|-----------|
| R-1 | Memory scraping of unlocked session | High | GC runtime, OS paging, process inspection — inherent to managed software |
| R-2 | Whole-file rollback to older header snapshot | Medium | Detectable post-unlock only; no trusted storage |
| R-3 | Old backup + old Recovery Key still opens old copy | Medium | Revocation is forward-only |
| R-4 | Weak user master password | High | Argon2id + strength feedback mitigate but user choice is the controlling factor |
| R-6 | No secure SSD erasure | Low | Application cannot control wear levelling |
| R-7 | Screen capture / keylogger while unlocked | High | Outside the application's security boundary |
| R-8 | Windows Clipboard History retention | Medium | App clears clipboard but cannot control OS-level history |
| R-10 | Portable drive removal during write | Medium | WAL + atomic writes minimize but don't eliminate the risk window |

## Limitations documented (not risks, but constraints)

1. .NET GC cannot guarantee complete buffer zeroization — see memory-security-audit.md
2. `object.ToString()` is annotated `string?` in .NET 10 — potential null-deref when combined
   with span slicing (found and fixed during review)
3. PowerShell `-Raw` re-encoding mangles UTF-8 source files (use the Edit tool)
4. NSec 26 package renamed from `NSec` to `NSec.Cryptography`; Argon2id supports p=1 only
5. Microsoft.Data.Sqlite removed: its bundled e_sqlite3 races the e_sqlcipher bundle
6. NSec 26's `SignatureAlgorithm.Ed25519.Verify` requires a `PublicKey` object, not raw bytes
