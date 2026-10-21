# ADR-007: Browser Extension Deferral

Status: Accepted | Date: 2026-10-22 | Decides: contract §1, §10

## Context
Browser autofill is the headline feature of most password managers, but it is also their widest
attack surface: a native-messaging host turns a private vault into a local service consumed by
an untrusted, network-exposed client. The contract defers the extension to a future phase and
prohibits premature implementation.

## Decision
**No browser extension, no native messaging, no autofill and no passkey code in v1.** The
architecture only *reserves* the seam:

- `VaultSession` keeps key material behind an `internal ISessionKeyProvider` so a future
  out-of-process host could request scoped access without redesign (architecture.md §7).
- Item payload envelopes are already field-purpose-tagged (AAD `purpose`), which a future
  autofill host will need for minimal disclosure ("give me username+password for this origin").
- No user-origin tracking, URL matching, or form-fill logic exists in v1 — precisely the data
  model additions that would be hard to remove later if built "just in case".

## Consequences
- v1 ships without the most demanded convenience feature; the roadmap compensates with
  friction-reducers that stay in-process (generator, TOTP copy, keyboard navigation).
- Deferred, not forgotten: extension work starts with its own threat model (native-messaging
  protocol design, origin-binding policy, revocation of host access) as a Phase-9+ ADR —
  not before the core is field-proven.
- Anything cloud/sync-shaped remains prohibited (contract §10) until a separate decision.
