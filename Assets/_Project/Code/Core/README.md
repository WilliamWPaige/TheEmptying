# Core

Simulation code. Plain C#, must run headless in edit-mode tests with no scene loaded.

Must never use:
- `UnityEngine` rendering (materials, meshes, cameras, shaders)
- `GameObject` / `MonoBehaviour` / scenes
- `Time.deltaTime` or any frame timing

See `CLAUDE.md` for the full architecture rules.
