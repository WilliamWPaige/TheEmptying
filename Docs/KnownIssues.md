# Known Issues

A short list of open bugs the user has deferred, so later prompts don't re-litigate them from scratch. Each entry: date opened, symptom, what's been tried, next step.

## UI click-through on the build menu

- **Opened:** 2026-11-20 (3.9b), still open as of 3.9f (2026-11-21).
- **Symptom:** clicking through the build menu (opening it, picking a type, clicking the menu's own buttons) sometimes still places a building in the world behind the menu, in the real Editor. Every automated attempt to reproduce this via simulated input has passed.
- **Tried:**
  - 3.9b/3.9c: added `UiInputUtility.IsPointerOverUi` (screen-to-panel conversion + `panel.Pick`) and wired it into every placement/inspect click path. Confirmed meaningful via sanity-check regressions (forcing the check off reliably causes a real click-through in the test).
  - 3.9d: added a real-input PlayMode test (`RealInputClickTests`) using `InputTestFixture`'s simulated `Mouse` device, driving `BuildingsPresenter`'s actual `Update()`/`Mouse.current` polling path. Discovered the simulated device updates `Mouse.current` correctly but never triggers UI Toolkit's own button `.clicked` dispatch, even after 60 frames - so the test can exercise the world-click-blocking logic for real, but can't drive "open the menu via a real click" end to end.
  - 3.9e: audited the three specific suspects named by the user (picking modes, panel scale vs. screen size, press/release event order). No `pickingMode` is set anywhere in the Presentation layer. Found and fixed a stale comment claiming the panel uses a 1:1 pixel scale - it actually uses Scale With Screen Size against a 1920x1080 reference - but `UiInputUtility` already uses the correct scale-aware conversion, so this wasn't the cause. Added `setting.debug.log_world_clicks` (on by default in the Editor): logs every left press and release with screen position, the `IsPointerOverUi` result, which UI element `panel.Pick` found, and what world action (if any) fired.
- **Next step:** the user pastes `[WorldClick]` log lines from a real repro (open the console in the Editor, reproduce the click-through, copy the relevant `[WorldClick]` lines). That log will show directly whether `overUi` was true or false and what `picked` was at the exact moment of the failing click, instead of further guessing.

## How to use this file

- When a prompt mentions a bug that looks like one of these, read the full entry before trying anything new - avoid repeating a "tried" approach.
- When an issue is resolved, move it out of this file and into the commit/report that fixes it (don't leave stale closed entries here).
- When a prompt defers a new bug instead of fixing it, add it here with the same four fields: date, symptom, what's been tried (even if "nothing yet"), next step.
