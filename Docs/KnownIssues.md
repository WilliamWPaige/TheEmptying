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

## WASD camera panning doesn't move the camera

- **Opened:** 2026-11-22 (3.10b4).
- **Symptom:** in the real Editor, W/A/S/D no longer pan the strategy camera. Zoom (mouse wheel) and clicking still work.
- **Tried:**
  - 3.10b4: compared every file that could plausibly be involved (`StrategyCameraController.cs`, the `TheEmptyingControls.inputactions` asset, the Game scene, ProjectSettings) against the last state the user confirmed this worked in (3.10a) - none of them differ at all. Checked for a UI panel stealing keyboard focus (People/Building/Stockpile panels have no `TextField` and never call `.Focus()`; all start hidden) and for anything disabling the Pan action or its map specifically (nothing else in the codebase references the Pan, Rotate or Zoom actions) - found nothing. Reasoned from the code that `_panAction` can't be null, since `StrategyCameraController.Update()` returns immediately if it is, which would also take Zoom and Rotate down with it, and the user reports Zoom still working.
  - Added logging behind a new `setting.debug.log_camera_input` setting (on by default in the Editor): on every press/release edge of W/A/S/D, logs the raw key states, the Pan action's read value, and whether the action and its map are enabled.
- **Next step:** the user reproduces the bug in the real Editor, then pastes the `[CameraInput]` log lines from around when they pressed a pan key. That will show directly whether the raw keys are even registering, and if they are, whether the Pan action's read value still comes back zero despite them - narrowing this from "somewhere in the whole input pipeline" to one specific stage.

## How to use this file

- When a prompt mentions a bug that looks like one of these, read the full entry before trying anything new - avoid repeating a "tried" approach.
- When an issue is resolved, move it out of this file and into the commit/report that fixes it (don't leave stale closed entries here).
- When a prompt defers a new bug instead of fixing it, add it here with the same four fields: date, symptom, what's been tried (even if "nothing yet"), next step.
