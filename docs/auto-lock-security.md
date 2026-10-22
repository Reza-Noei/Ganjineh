# Auto-Lock Security — as built (Phase 7)

## Measurement

Monotonic elapsed time via `Environment.TickCount64` — immune to system clock changes and
sleep/resume jumps. Activity detection resets the idle timer on keyboard and mouse input
(`InputManager.PreProcessInput`); background timers and async completions do NOT count as
activity.

## Policies (per AppSettings)

| Policy | Default | Values |
|--------|---------|--------|
| Lock after idle | 10 min | 1/5/10/15/30/Never |
| Lock on Windows lock | on | on/off |
| Lock on minimize | off | on/off |
| Lock on suspend | on | on/off |
| Lock on exit | always | (not configurable) |

## Bypass resistance

The lock path operates on the workspace state machine directly — it cancels pending operations
by disposing the session, which invalidates the generation counter. A pending async operation
holding a stale session reference will throw `StaleSessionException` on the next service call
and cannot repopulate UI state.
