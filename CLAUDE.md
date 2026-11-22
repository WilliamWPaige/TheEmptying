# The Emptying — Project Guide for Claude Code

Read this file at the start of every session. It is the rulebook for this project.

## What this is

The Emptying is a single-player sandbox colony sim in Unity. The player is a relic in a keep, ruling a kingdom through the monarchs it influences. The player never directly controls people: they act on laws, rules, incentives, permissions and their own personalities. Reference games: Songs of Syx, RimWorld, Oxygen Not Included, Tropico, Dwarf Fortress.

- Team: William (creator, decision-maker) and Claude (programming). Art comes later, from William's Synty and other store packs.
- Engine: Unity 6000.6.8f1, Universal Render Pipeline. Do not change the Unity version.
- The design is LOCKED. Build what the docs say. If something in the docs is unclear, contradictory or seems wrong, stop and ask. Do not invent design.
- The design locks in layers: each later layer (building management in full, settlements, military, religion, and so on) gets its own Phase 3 design and Phase 4 review before it is built. Only the milestone that's been locked (see the Phase 5 build plan) is built now; a system with no lock yet is not ready to build.
- The town simulation (everything built in Milestone 3, prompt 3.3 onward) was removed in R0 (2026-11-22) and is being rebuilt from the Milestone 2 base, one system at a time, in prompts numbered R1, R2, and so on. The design docs and their lock are unchanged; only the code is being rebuilt. The pre-rebuild code is tagged `before-rebuild` in git for reference.
- Before starting work, check `Docs/KnownIssues.md` for open bugs the user has deferred. If a prompt touches one, read its full entry (what's been tried, next step) before trying anything new.

## Design docs (source of truth)

All in `Docs/Design/`:

| File | Use it for |
| --- | --- |
| `01-Concept.pdf` | What the game is |
| `02-Systems.pdf` | Every system and how they connect |
| `03-SystemDesign.pdf` | Detailed rules and numbers for the colony sim |
| `03b-SystemDesign-Earlier.pdf` | Older detailed design. Only sections that `03-SystemDesign.pdf` says carry over apply; everything else is history |
| `04-ReviewAndLock.pdf` | Locked decisions, build order, engine and tools |
| `05-TechnicalFramework.pdf` | Architecture, modules, events, rule system, build plan. Follow this for every technical decision |

Docs beat code. If a task shows a spec is wrong or incomplete, say so and propose the doc change. Design-level questions go to William.

The PDFs above are exports and can go stale. At the start of every prompt, read the design sections that prompt needs from the live docs through the Claude Docs connector, not the PDFs. The live docs (owned by William, Claude Docs artifacts):

| File | Live doc |
| --- | --- |
| `01-Concept.pdf` | https://claude.ai/artifact/QJcdiqfvc7cwBHwjVfBzT7 |
| `02-Systems.pdf` | https://claude.ai/artifact/WbrazKPeCvBNwbEaHL2P3u |
| `03-SystemDesign.pdf` | https://claude.ai/artifact/4ut8WiQFYSCFLiA78QZzi2 |
| `04-ReviewAndLock.pdf` | https://claude.ai/artifact/LTPrYVJgSvuNoX5sFe1aw7 |
| `05-TechnicalFramework.pdf` | https://claude.ai/artifact/GBX7xrwi7mLWtVyysCAJ6A |

`03b-SystemDesign-Earlier.pdf` has no live doc; it's frozen history and only read for the sections `03-SystemDesign.pdf` says carry over. If a live doc's content has changed since its PDF in `Docs/Design/` was exported, re-export that PDF (via the Claude Docs connector's export, as PDF, overwriting the file in place) before using it, so the PDFs stay a usable fallback and a record of what was built against.

## Architecture rules (never break these)

### Three layers

| Layer | Folder | May know about |
| --- | --- | --- |
| Data | `Assets/_Project/Data`, `Assets/_Project/Rules` | Nothing else |
| Simulation | `Assets/_Project/Code/Core`, `Assets/_Project/Code/Modules` | Data only. NEVER `UnityEngine` rendering, GameObjects, MonoBehaviours, scenes, Time.deltaTime or frame rate |
| Presentation | `Assets/_Project/Code/Presentation`, `Assets/_Project/UI` | Reads the simulation and sends player actions to it |

The simulation is plain C#. It must run headless, in edit-mode tests, with no scene loaded.

### Simulation clock and determinism

- Fixed tick: 10 ticks per in-game minute. Speeds are 1x, 2x and 3x only (20, 40, 60 ticks per real second at the default 12 real minutes per in-game day).
- Never use `UnityEngine.Random`, `System.Random` without our seeded wrapper, `DateTime.Now`, or frame timing inside the simulation. Use the simulation's seeded random source.
- Same save plus same inputs must always produce the same result.
- Systems update on their own intervals, staggered across ticks. People update by importance: stable, content people rarely; important, troubled or on-screen people often.

### Modules

- Every system is a module in its own folder under `Code/Modules/<ModuleName>/` with its own assembly definition.
- A module may reference only Core and the modules it declares as dependencies. Never reference another module without declaring it.
- Every module has an on/off switch, a dependency list and its own settings. Switching a module off must not break anything else.
- Entities (people, buildings, items) are IDs. Each module stores its own data in its own tables keyed by entity ID. Never add module-specific fields to a shared entity class.
- Each module saves and loads its own section of the save file.

### Events

- Modules never call each other directly. They post typed events to the event bus and listen for the events they need.
- Events are queued during a tick and delivered at the end of the tick.
- Every event type has a visibility (public, witnessed, hidden) and editor-set scores (reporting threshold, notability). The chronicle, the herald, laws and punishments only see events the crown knows about.

### Data and content

- No mod support. The content loader still loads the game's own core data folder the same way it always has; nothing is promised to outsiders.
- All content is data, never hard-coded: resources, items, buildings, traits, laws, faiths, events, techs, relics, names.
- Player-facing content (buildings, resources, races, faiths, needs and the like) is authored as ScriptableObjects in the Inspector. At startup the presentation layer copies each one into a plain data record; the simulation only ever sees those records, so it stays free of `UnityEngine` and deterministic. ScriptableObjects are never changed during play.
- Technical definitions the simulation reads directly — event definitions, settings, rules — stay as JSON files in the core content source, maintained by Claude Code.
- Every content item has a permanent, readable text ID such as `resource.iron_ore`. IDs are never renamed or reused.
- Any tunable number should be a named setting, so it can appear as a slider in the custom difficulty panel. Log new settings in the slider registry.
- Settings are for tunable numbers known at startup, not for anything that changes during play. Anything that changes during play (a discovered building type's allowed status, maximums and the like) is module state, saved in that module's own save section like any other per-entity data — never a setting, even a runtime-declared one, since the core settings section is restored before any module gets its own data back and can't correctly round-trip a value nothing has declared yet.

### The rule system

- Behavior (laws, punishments, events, decision popups) is built from blocks: triggers, conditions, actions and flow.
- Rules are data: JSON files Claude Code writes from William's plain-English descriptions, in the same format the rule editor saves, so they still open and inspect cleanly there. Do not hard-code behavior that should be a rule.
- The rule editor is frozen as a viewer and test tool: open a rule to see its shape, run it in the Test tab, and watch it with Live Tracing. No further editor features are planned; engine work on the rule system itself (new blocks, performance, fixes) continues as needed.

## Folder structure

```
Assets/
  _Project/
    Code/
      Core/          clock, events, modules, data loading, saves, random, rule system runtime
      Modules/       one folder per system module
      Presentation/  Unity-facing code: views, camera, input, visuals
      Editor/        editor tools: rule editor, importer, inspectors
    Data/            ScriptableObject content
    Content/         ScriptableObject content assets (Units, ResourceNodes, Buildings, Items, Shapes)
    Rules/           rule data
    Scenes/          MainMenu and Game only
    Prefabs/
    UI/              UI Toolkit layouts (.uxml) and styles (.uss)
    Tests/
      EditMode/      simulation tests (most tests live here)
      PlayMode/      tests that need a running scene
  ThirdParty/        store asset packs. NEVER commit, NEVER edit
Docs/
  Design/            locked design docs
```

## Conventions

- C#: PascalCase for types, methods and properties; camelCase for locals and parameters; `_camelCase` for private fields.
- Namespaces follow folders: `TheEmptying.Core`, `TheEmptying.Modules.Needs`, `TheEmptying.Presentation`.
- One public type per file; file name matches the type.
- UI uses UI Toolkit (UXML and USS). The older uGUI only for things floating in the 3D world.
- Input uses the Input System package, with every action rebindable.
- Keep classes small and focused. Prefer plain data structs for simulation state.
- Always compare and sort strings with ordinal comparison in the simulation. Never use culture-sensitive comparison; it breaks determinism across players' language settings.

## Tests

- William does all behavior testing by hand in the real Editor. Only write new tests for data: content files, settings, schemas, and save/load round trips. Do not write new simulation or behavior tests.
- Tests must be deterministic: seed every random source.
- Before every commit, run the existing EditMode suite and make sure it passes. Report the results.
- Do not run PlayMode tests unless the prompt asks for it.

## Running tests

Unity must be fully closed first (batch mode cannot share a project with an open editor instance).

Before running tests, check for `Unity.exe` processes whose command line includes this project's path (`D:\Unity_Projects\TheEmptying`). If any exist, close them without asking. Never close Unity processes for other projects; if you can't tell which project a Unity process belongs to, ask.

```
"C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" -runTests -batchmode -nographics -projectPath "D:\Unity_Projects\TheEmptying" -testPlatform EditMode -testResults "D:\Unity_Projects\TheEmptying\Logs\editmode-results.xml" -logFile "D:\Unity_Projects\TheEmptying\Logs\editmode-run.log"
```

Check the exit code, then read `Logs/editmode-results.xml` (`result="Passed"` / `result="Failed"` on the root `<test-run>` element) and `Logs/editmode-run.log` for compiler errors.

PlayMode tests load real scenes, so they need the same process check and a graphics device; run them the same way with `-testPlatform PlayMode`:

```
"C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" -runTests -batchmode -projectPath "D:\Unity_Projects\TheEmptying" -testPlatform PlayMode -testResults "D:\Unity_Projects\TheEmptying\Logs\playmode-results.xml" -logFile "D:\Unity_Projects\TheEmptying\Logs\playmode-run.log"
```

Check `Logs/playmode-results.xml` and `Logs/playmode-run.log` the same way.

## Developer tools

The in-game console and inspector are part of the core. When you add a module, add console commands and inspector panels for its data where useful.

## Module checklist

What a new module needs, based on what the milestone-1 demo module (`module.demo_hunger`, deleted in milestone 3) needed to prove the framework end to end:

- **Folder and assembly definition.** `Code/Modules/<Name>/` with its own `.asmdef`, referencing `TheEmptying.Core` and only the modules it declares as dependencies.
- **A manifest in data, not code.** A `module_manifest` item (id, version, dependencies, enabled) in a `Data/*.json` file in the core content source. A module with no matching manifest in the data is a build error; a manifest with no matching module code is too.
- **`IModule`**: `Id`, `Version`, `Dependencies`, `Initialize(ModuleContext)`, `Shutdown()`. Add `IModuleDeclaration` only if the module needs to register its own content type or declare something before content loads; settings and event definitions normally belong in data instead (see below).
- **Settings and events belong in data.** Declare a setting as a `setting_definition` content item and an event as an `event_definition` content item, not in code. Read a setting's live value with `context.Settings.Get(id)` inside the system that needs it every time it runs — never cache the value once, since the player (or a test) can change it mid-game.
- **Entities and module data.** Store per-entity data in an `EntityTable<T>` created with `new EntityTable<T>(context.Entities)`. Only create entities on a new game (`if (!context.IsLoading)`); on a load, the table is repopulated by `Read`.
- **Scheduling.** Register scheduled systems with `context.RegisterSystem(...)`. For per-entity work, use `BucketedWork` with the tick itself (not a module-local counter) as the run number, so the bucket and importance-tier schedule lines up exactly the same whether the game ran straight through or was saved and loaded partway.
- **Saving**: implement `ISaveableModule` (`Write`/`Read`). Write exactly what's needed to reconstruct the module's `EntityTable` (and anything derived from it, like an index lookup) on `Read` — a save/load round trip must reproduce the exact same state hash.
- **Inspection**: implement `IInspectableModule.Inspect(EntityId)` for the "inspect" console command and the inspector.
- **Console commands**: register with `context.RegisterCommand(...)`. A command with wrong or missing arguments returns its own "Usage: ..." line, not the full help list.
- **Events**: post through `context.Events`. A data event (`EventData`, posted by definition ID) saves itself automatically if it's still pending at save time; a typed `ISimEvent` struct needs a registered `ITypedEventSerializer` to be saveable at all.
- **Tests**: edit-mode tests named after the module, including a save-and-load round trip. If the module reads a setting or contributes to the state hash, also test determinism: the same seed gives the same hash, and an interrupted run (save, load, continue) matches an unbroken run of the same length.

## Known pitfalls

- If keyboard input is dead in Play mode but the mouse works, it is the Unity Editor focus issue (Play Maximized), not code. The workaround: Run In Background on, Input System Background Behavior = Ignore Focus, Play Mode Input Behavior = All Device Input Always Goes To Game View. Check these before debugging input code.
- A content problem (a missing or duplicate Id, a wrong prefix, an empty display name, a footprint below 1, a missing Model) stops the game from starting, by design (R5b). It is not a bug: the game shows a full-screen list of every problem instead of loading. Fix the content, not the code, unless the screen itself is wrong.
- The camera's zoom-out is limited by design (R6b): only the area near the camera is ever rendered, so chunks past setting.map.view_distance_chunks are never built or shown. A small, closely-zoomed view of a big map is intentional, not a bug.

## How we work

- One thing per prompt. Do only what the prompt asks. If you notice something else that needs doing, note it under a "Noticed, not fixed" heading in your report instead of doing it.
- Stop rule: if a fix doesn't work after your first serious attempt, stop. Report what you found, what you tried, and your best guesses at the cause. Do not keep looping or try a long series of alternatives.
- Specs: when a design doc section lists states and transitions for a behavior, implement exactly that list. If it's ambiguous or seems wrong, stop and ask instead of inventing logic.
- Before writing code, say briefly which files you will create or change.
- Never touch `Assets/ThirdParty/`, `Library/`, or Unity version settings.
- Never add packages or third-party code without asking.
- Never commit or push unless the prompt says to. When asked, use clear commit messages describing what changed.
- Reporting: keep messages and the final report in plain language. Name the files changed and say what changed and why, including test results, anything left open, and any place the docs and the code disagree. Do not paste code excerpts in messages or reports; William will read the code if needed.
- New ideas from William go through a mini review (scope, what it touches, fit with the locked design) before they enter the docs or the code.
