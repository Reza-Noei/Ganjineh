# Password & Passphrase Generator + Strength Estimation — as built (Phase 5)

See [phase-5-implementation.md](phase-5-implementation.md) for the full record; per-contract
entry points:

## password-generator.md

- CSPRNG: `RandomNumberGenerator.GetInt32` exclusively (rejection sampling, unbiased).
- Constraints validated pre-generation: length 4–256, ≥1 class, per-class minimums ≤ length,
  non-empty pools after exclusions; violations → `GenerationException` (localized).
- Minimum counts: satisfied per class, remainder from union, unbiased Fisher–Yates shuffle.
  Distribution: uniform random permutation of a uniform random constrained multiset; statistical
  band test (±4σ over 20 k samples) passes.
- Entropy: `len × log2(|pool|)` — documented conservative estimate (minimums reduce the true
  space; the delta is bounded by the minima and never claimed).
- Generated passwords are never persisted or logged; clipboard copies use the TTL service.

## passphrase-generator.md

- Wordlist: EFF Large Wordlist (2016), 7,776 unique lowercase words (four hyphenated),
  integrity-checked at every construction (count + uniqueness, else refuse to start).
- License: CC BY 4.0 (attribution file shipped beside the embedded resource).
- Entropy: words × log2(7776) ≈ 12.925 bits/word; +log2(10) for the optional suffix.
- English only. A Persian wordlist requires audited uniqueness/normalization/distribution —
  explicitly deferred; no invented list ships.

## password-strength-estimation.md

- Library: **zxcvbn-core 7.0.92** (MIT) — offline matching/scoring/feedback.
- `IPasswordStrengthEstimator.Evaluate` → score 0–4 + localized warning/suggestions.
- Persian feedback via mapping of the known zxcvbn warning set onto resource keys.
- Scores are relative resistance estimates; the UI never claims uncrackability, never shows
  exact crack times as guarantees, and never sends passwords anywhere (no network code in the
  estimator path; verified by the offline test suite).
