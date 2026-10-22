# Phase 5 Test Results

Date: 2026-10-22 | Suite: `dotnet test PasswordManager.slnx` — **195/195 green**, zero warnings.

## New coverage (Phase 5)

| Area | Tests | Highlights |
|------|-------|-----------|
| Password generator | 9 | length/class/min-count constraints, exclusions, ambiguous removal, impossible configs rejected, statistical distribution (±4σ over 20 k samples), presets, non-repetition |
| Passphrase | 5 | wordlist integrity (7 776 unique), count/separator/capitalize/suffix, entropy = words × log2(7776) |
| Strength estimator | 2 | known bands (`password`→VeryWeak, `correcthorsebatterystaple`→VeryStrong), Unicode/Persian inputs, no crash on empty |
| TOTP | 7 | RFC 6238 Appendix B vectors (SHA-1/256/512, T=59 & T=1111111109), window boundaries, seconds-remaining countdown, Base32 RFC 4648 vectors, invalid secret/digit/period rejection, drift visibility |
| HOTP | 10 | RFC 4226 Appendix D vectors (counters 0–9) |
| otpauth import | 2 | full URI parse (issuer/account/alg/digits/period); 7 malformed-URI rejection cases |
| TOTP storage | 1 | config round-trip through Data keys; code from restored config == code from original; secret encrypted at rest |
| Security analyzer | 5 | weak (Critical/High severity rules), reuse groups, empty-password handling (no stacking), missing-authenticator phrasing, expiring items, locked-session rejection, no plaintext in report |

## Full-suite totals

195 tests across 7 assemblies: Domain 5, Persistence 4, Application 2, Cryptography 104
(incl. 15 new generator/passphrase tests, 20 new OTP tests incl. all RFC vectors),
Integration 72 (19 new Phase 5), Security 7, Desktop.UITests 1.

## Issues found and fixed during the phase

1. RFC 6238 test harness initially passed the vector times in the wrong unit — corrected to
   seconds (engine was already correct against the RFC values).
2. Base32 unpadded RFC 4648 expectations were mis-remembered in the first test draft — corrected
   to the canonical stripped forms.
3. Wordlist loader rejected the four hyphenated EFF words — filter widened to lowercase+hyphen
   with the 7 776-unique integrity check retained.

## Known limitations (documented, deferred)

- Persian passphrase wordlist: deferred pending an audited list (no invented list ships).
- QR-image import: deferred (no bundled offline decoder dependency).
- HOTP has service-level implementation + tests; dedicated item UI is Phase 6+ scope.
- Strength estimator dictionaries are English-centric (Persian inputs still length/entropy-scored).
