# The Emptying — Project Guide for Claude Code

Read this file at the start of every session. It is the rulebook for this project.

## What this is

The Emptying is a single-player sandbox colony sim in Unity. The player is a relic in a keep, ruling a kingdom through the monarchs it influences. The player never directly controls people: they act on laws, rules, incentives, permissions and their own personalities. Reference games: Songs of Syx, RimWorld, Oxygen Not Included, Tropico, Dwarf Fortress.

- Team: William (creator, decision-maker) and Claude (programming). Art comes later, from William's Synty and other store packs.
- Engine: Unity 6000.6.8f1, Universal Render Pipeline. Do not change the Unity version.
- The design is LOCKED. Build what the docs say. If something in the docs is unclear, contradictory or seems wrong, stop and ask. Do not invent design.

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

- All content is data, never hard-coded: resources, items, buildings, traits, laws, faiths, events, techs, relics, names.
- Content is ScriptableObjects in the editor and JSON for mods; both load into the same internal form.
- Every content item has a permanent, readable text ID such as `resource.iron_ore`. IDs are never renamed or reused.
- The base game is itself a mod: a core data folder loaded the same way as any mod.
- Any tunable number should be a named setting, so it can appear as a slider in the custom difficulty panel. Log new settings in the slider registry.

### The rule system

- Behavior (laws, punishments, events, decision popups) is built from blocks: triggers, conditions, actions and flow.
- Rules are data, authored in our node editor. Do not hard-code behavior that should be a rule.

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

- Every simulation feature ships with edit-mode tests in `Tests/EditMode/`, named after the module (`NeedsTests`, `EventBusTests`).
- Tests must be deterministic: seed every random source.
- A task is not done until its tests pass. Report the test results.

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
- **A manifest in data, not code.** A `module_manifest` item (id, version, dependencies, enabled) in a `Data/*.json` file in the core content source (or a mod, to override it). A module with no matching manifest in the data is a build error; a manifest with no matching module code is too.
- **`IModule`**: `Id`, `Version`, `Dependencies`, `Initialize(ModuleContext)`, `Shutdown()`. Add `IModuleDeclaration` only if the module needs to register its own content type or declare something before content loads; settings and event definitions normally belong in data instead (see below).
- **Settings and events belong in data.** Declare a setting as a `setting_definition` content item and an event as an `event_definition` content item, not in code. Read a setting's live value with `context.Settings.Get(id)` inside the system that needs it every time it runs — never cache the value once, since the player (or a test) can change it mid-game.
- **Entities and module data.** Store per-entity data in an `EntityTable<T>` created with `new EntityTable<T>(context.Entities)`. Only create entities on a new game (`if (!context.IsLoading)`); on a load, the table is repopulated by `Read`.
- **Scheduling.** Register scheduled systems with `context.RegisterSystem(...)`. For per-entity work, use `BucketedWork` with the tick itself (not a module-local counter) as the run number, so the bucket and importance-tier schedule lines up exactly the same whether the game ran straight through or was saved and loaded partway.
- **Saving**: implement `ISaveableModule` (`Write`/`Read`). Write exactly what's needed to reconstruct the module's `EntityTable` (and anything derived from it, like an index lookup) on `Read` — a save/load round trip must reproduce the exact same state hash.
- **Inspection**: implement `IInspectableModule.Inspect(EntityId)` for the "inspect" console command and the inspector.
- **Console commands**: register with `context.RegisterCommand(...)`. A command with wrong or missing arguments returns its own "Usage: ..." line, not the full help list.
- **Events**: post through `context.Events`. A data event (`EventData`, posted by definition ID) saves itself automatically if it's still pending at save time; a typed `ISimEvent` struct needs a registered `ITypedEventSerializer` to be saveable at all.
- **Tests**: edit-mode tests named after the module, including a save-and-load round trip. If the module reads a setting or contributes to the state hash, also test determinism: the same seed gives the same hash, and an interrupted run (save, load, continue) matches an unbroken run of the same length.

## How we work

- One task per prompt. Do what the prompt asks, nothing more. If you notice something else that needs doing, list it at the end instead of doing it.
- Before writing code, say briefly which files you will create or change.
- Never touch `Assets/ThirdParty/`, `Library/`, or Unity version settings.
- Never add packages or third-party code without asking.
- Never commit or push unless the prompt says to. When asked, use clear commit messages describing what changed.
- At the end of each task, report: what you built, files changed, test results, anything left open, and any place the docs and the code disagree.
- New ideas from William go through a mini review (scope, what it touches, fit with the locked design) before they enter the docs or the code.
