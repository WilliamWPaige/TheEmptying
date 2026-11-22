# Known Issues

A short list of open bugs the user has deferred, so later prompts don't re-litigate them from scratch. Each entry: date opened, symptom, what's been tried, next step.

(Empty as of R0, 2026-11-22: the two entries that were here - the build menu's UI click-through and WASD camera panning - were closed because the code they were about, the build menu, `BuildingsPresenter`, `UiInputUtility`, the strategy camera and `StrategyCameraController`, was removed along with the rest of the Milestone 3 town simulation for the rebuild. See the R0 commit.)

## How to use this file

- When a prompt mentions a bug that looks like one of these, read the full entry before trying anything new - avoid repeating a "tried" approach.
- When an issue is resolved, move it out of this file and into the commit/report that fixes it (don't leave stale closed entries here).
- When a prompt defers a new bug instead of fixing it, add it here with the same four fields: date, symptom, what's been tried (even if "nothing yet"), next step.
