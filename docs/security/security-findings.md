# Security Findings — Phase 8 Register

All findings from the adversarial review, their status, and verification evidence are
consolidated in [security-findings.md](security-findings.md). The register uses the following
documented severity methodology:

- **Critical**: plaintext secret exposure, authentication bypass, or data corruption
- **High**: partial secret exposure, integrity violation, or recovery failure
- **Medium**: defence-in-depth gap, format oracle, or incomplete metadata validation
- **Low**: readability, consistency, or minor hardening gap
- **Informational**: by-design behaviour worth documenting

## Summary

| ID | Severity | Status |
|----|----------|--------|
| F-01 Chunk tag skip | High | **Fixed** (regression: Phase6Tests.Adversarial) |
| F-02 DbSha256 full hex | High | **Fixed** (regression: Phase6Tests.Backup_Verify) |
| F-03 Package offset −8 | Medium | **Fixed** (regression: BackupChunkDiag) |
| F-04 Format oracle | Medium | **Fixed** (regression: Wrong_Credential_Is_Rejected) |
| F-05 Code readability | Low | **Fixed** |
| F-06 Empty attachment package | Informational | Open (by design) |
| F-07 GC secret copies | Informational | Accepted (R-1) |

No unresolved Critical or High findings. No known data-loss defects.
