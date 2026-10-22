# Phase 5 Implementation — Generator, Authenticator, Security Center

Status: Implemented and tested (phase-5-test-results.md) | 2026-10-22

## 1. Password Generator (`Cryptography/Generation`)

- **Engine:** every random decision uses `RandomNumberGenerator.GetInt32` (rejection sampling —
  no modulo bias). Category minimums are satisfied first, the remainder is drawn from the union
  pool, then an unbiased Fisher–Yates shuffle runs. Distribution behavior is statistically
  tested (20 k-sample per-digit chi-band ±4σ).
- Options: length 8–64 in UI (4–256 engine), class toggles, ambiguous-exclusion
  (Il1|0Oo{}[]()/\'"`,~;:.<>|), free-text exclusions, per-class minimum counts; impossible
  combinations throw typed `GenerationException`s surfaced in Persian.
- **Entropy reporting:** `length × log2(poolSize)` — the conservative unconstrained estimate;
  per-class minimums slightly reduce the true space (documented in password-generator.md).
- **Presets:** Standard (16, l/u/d), Strong (24, l/u/d/s), Maximum Compatibility (20, l/u/d,
  no ambiguous), Numeric PIN (8 digits), Custom. The PIN preset warns it is for PIN fields, not
  account passwords.

## 2. Passphrase Generator

EFF Large Wordlist v2016 (**7,776 unique words**, verified at load: count + uniqueness integrity
check; CC BY 4.0 — attribution embedded). Words drawn via GetInt32; entropy = words × log2(7776)
(+ log2(10) for the optional number suffix). Options: 3–12 words, separator, capitalization,
number suffix. English only; a Persian list would require audited normalization/uniqueness —
deferred (documented).

## 3. Strength estimation

**zxcvbn-core 7.0.92** (MIT, trichards57 — the maintained .NET port of the zxcvbn model), fully
offline. Wrapped as `IPasswordStrengthEstimator`; known zxcvbn English warnings are mapped onto
Persian localization keys. Scores are relative estimates (0–4), never cracking-time guarantees.
`correcthorsebatterystaple` scores VeryStrong; `password`/`123456`/`aaaaaaaaaa` VeryWeak (tested).

## 4. TOTP/HOTP (`Cryptography/Otp` + `Application/Security/OtpService.cs`)

- **Engine:** RFC 4226 dynamic truncation over BCL HMACSHA1/256/512; TOTP counter = UTC Unix
  time / period; testable via explicit `DateTimeOffset`. Verified against RFC 4226 Appendix D
  (10 vectors) and RFC 6238 Appendix B (SHA-1/256/512 @ T=59 and T=1111111109).
- **Base32** RFC 4648 (padding-tolerant decode) with RFC vectors.
- **otpauth:// import** — validates scheme (totp only), secret (Base32, ≥8 bytes, ≤200 chars),
  algorithm, digits (6–8), period (1–3600 s), URI length cap; malformed input → Persian errors.
- **Storage:** TOTP config lives in the Login payload Data keys (`totpSecret` Base32, issuer,
  account, algorithm, digits, period) — inside the existing item envelope, bound to Vault+item.
  HOTP counter keys are handled transactionally through the item-service update path; the
  persisted counter advances only on real saves, never on UI refresh.
- **UI:** details pane shows code + seconds-remaining + copy with a 1 s DispatcherTimer (only
  while a TOTP item is selected); codes and the timer stop and clear on lock/switch; the factor-
  separation warning is displayed under the code. QR-image import: deferred (no bundled decoding
  dependency — contract allows).

## 5. Security Center (`Application/Security/VaultSecurityAnalyzer.cs`)

Analyzes the unlocked session only (locked → `StaleSessionException`); per-vault by construction.
Findings (no password material in any DTO): Weak (zxcvbn ≤2; Critical when also reused/very-weak,
else High), Reused (exact-match groups via ephemeral MD5 fingerprints — in-memory only), Empty
password (Medium, single finding, no stacking), Old >365 days (Informational — age is
informational, never "critical"), Missing authenticator (Informational, phrased "no authenticator
stored"), Duplicate account (normalized username+title, Medium, review-only), Expiring secrets
(API/License/Card `expiresAt` within 30 days → Medium; expired → High).
Aggregate 0–100 score = 100 − Σ(20/10/5/0 per severity), capped — documented as a local hygiene
indicator with no security guarantee. Analysis runs off the UI thread with cancellation tokens;
results are discarded on lock/switch (session-boundary wipe).

## 6. UI

Generator overlay (password/passphrase tabs, presets via config, length/word sliders, live
entropy, copy, regenerate, use-in-editor), Security Center dashboard (score, per-finding tiles,
findings list with click-to-item), TOTP section in Login details. All Persian-first RTL, Phase 1
design system, automation ids for FlaUI.
