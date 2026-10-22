# Security Center — as built (Phase 5)

## Findings and severities (documented criteria)

| Finding | Detection | Severity |
|---------|-----------|----------|
| Weak password | zxcvbn score ≤ 2 | **Critical** when also reused or score = 0; else **High** |
| Reused password | exact-match groups inside the unlocked vault | High |
| Empty password | login with no stored password | Medium (intentional passwordless auth is acknowledged in the UI text) |
| Duplicate account | normalized username + normalized title groups | Medium (review-only; never auto-merged) |
| Old password | unchanged > 365 days | **Informational** — age alone never escalates to critical (contract §8.4) |
| Missing authenticator | login without stored TOTP seed | **Informational** — phrased "no authenticator stored", never "2FA disabled" |
| Expiring secret | API/License/Card `expiresAt` within 30 days | Medium; expired → High |

Analysis is bound to the active unlocked session; findings carry item ID/title/kind/severity —
never password plaintext. The ephemeral reuse-comparison fingerprints (MD5 of the password,
in-memory only) exist solely to group duplicates and are discarded with the session; nothing is
persisted. Cross-vault analysis is impossible (single-session model, per-vault DB).

## Dashboard

Persian RTL dashboard: aggregate score (0–100 = 100 − Σ penalties; Critical −20 / High −10 /
Medium −5 / Informational −0; capped at 0). The score is explicitly labeled "راهنما، نه تضمین"
(a guide, not a guarantee) — the formula and its limits are documented here. Tiles: logins,
weak, reused, expiring. Findings list is filterable by kind and each entry navigates to its item.
Analysis runs on a background task with cancellation; stale results are discarded on session
boundaries.

## Recommendations shown

Weak → length/variety suggestion (strength estimator feedback); Reused → unique password per
account; Expiring → rotate the affected secret. Old-password findings explicitly avoid routine-
rotation advice (per §8.4: age is informational, not proof of compromise).
