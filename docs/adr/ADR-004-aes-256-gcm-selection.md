# ADR-004: AES-256-GCM as the Authenticated Encryption Primitive

Status: Proposed | Date: 2026-10-22 | Decides: contract §3

## Context
All program data (VMK wraps, item payloads, attachments, backups, header authentication) needs
confidentiality **and** integrity with explicit binding to context (vault, record, purpose,
version). .NET offers `AesGcm` natively with hardware acceleration.

## Decision
**AES-256-GCM** (96-bit nonce, 128-bit tag) via `System.Security.Cryptography.AesGcm` for every
envelope and wrap record; HMAC-SHA-256 for the header MAC; HKDF-SHA-256 for all key derivation.
Nonce policy: random 96-bit for wraps and item envelopes (collision math documented,
crypto-spec §5); structurally collision-free counter-derived nonces for attachment chunks
(TLS-1.3-style IV ⊕ counter). AAD normatively binds version, key domain, VaultID, record ID and
purpose (crypto-spec §7) — splicing attacks fail authentication by construction.

## Alternatives Considered
- **AES-256-GCM-SIV (misuse-resistant)** — attractive for nonce-reuse safety, but not in the
  BCL; adding a second crypto dependency for a risk we already handle structurally (counter
  nonces) + random-nonce analysis is not justified in v1. Revisit if a format v2 ever prefers it.
- **ChaCha20-Poly1305** — equal security; AES wins on Windows desktops with AES-NI.
- **CBC + HMAC (encrypt-then-MAC)** — correct but more moving parts; GCM is single-pass, native.
- **AES-KW for key wrapping** — no AAD support; GCM wraps give us header authentication for free.

## Consequences
- Random-nonce ceilings are documented: at 10⁶ envelopes/vault, collision probability ≈ 4×10⁻¹⁷
  (crypto-spec §5); attachments avoid the issue entirely.
- GCM ciphertexts are malleable-in-theory/always-detected: tag verification before any plaintext
  release, generic error, no oracle differentiation.
- .NET `AesGcm` tag check is constant-time-ish; no additional hardening required for v1 threat model.
