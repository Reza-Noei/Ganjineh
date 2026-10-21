# Secure Memory Limitations — honest limits of the .NET implementation

Status: Phase 2 | Companion to threat-model.md R-1. Read before making security claims.

## What the implementation does

- All key material lives in `KeyMaterial` (owned `byte[]`) or `VaultKeyring`; `Dispose` calls
  `CryptographicOperations.ZeroMemory` and releases the reference.
- Passwords enter as `char[]` buffers (Phase 3 UI will hand over `PasswordBox` buffers directly);
  UTF-8 conversion allocates a dedicated buffer that is zeroized after KDF.
- KEKs (PEK/RKEK) are zeroized immediately after unwrap; VMK copies are zeroized at the end of
  derivation flows; recovery secrets are zeroized by the caller after single display.
- No secrets in `string` fields, logs, exception messages (verified by SecretHygieneTests), or
  crash dumps (dump creation is disabled by default; NFR-5).

## What a garbage-collected runtime cannot guarantee

1. **GC relocation**: the GC may copy a `byte[]` during compaction; the stale copy is not
   zeroizable and lingers until that memory is reused. Pinned buffers would stop compaction but
   fragment the heap and are not used for short-lived derivation buffers in v1.
2. **Transient duplicates**: span operations, runtime internals, or JIT optimizations may create
   short-lived copies outside our control.
3. **Pagefile / hibernation**: secret memory can reach `pagefile.sys` or `hiberfil.sys`; .NET
   does not expose VirtualLock for managed arrays, and we do not P/Invoke it in v1. Full-disk
   encryption is the system-level mitigation; documented, not solved in-app.
4. **Process memory disclosure**: while a vault is unlocked, its keys are in RAM — any attacker
   with the privileges of the process (malware running as the user) can read them. This is
   accepted residual risk R-1; the at-rest design assumes the OS is not compromised (threat-model §1).

## Practical consequences (already encoded in the design)

- Keys exist only while a vault is unlocked; `Lock` is exposed prominently and triggered by idle
  timeout / Windows session lock (Phase 3 wiring).
- The registry and settings never contain secrets (ST-DB-4); nothing secret persists outside the
  cipher envelope at any point (ST-DB-2 sentinel scans over header/DB/WAL/shm/registry).
- Future hardening options (post-v1): pinned+POH buffers with explicit wipe, working-set locking
  via P/Invoke, memory-only salt caching policies, process-wide minidump filter.

## Verification status

Zeroization of reachable buffers is unit-tested (`KeyMaterial_Dispose_Zeroizes_Buffer`,
`Dispose_Zeroizes_Keyring`); the *absence* of all stale GC copies is **not** testable and is not
claimed. Testing does not prove the absence of side-channel or memory-disclosure vulnerabilities
(security-test-plan.md §1 note).
