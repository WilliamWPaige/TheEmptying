# Milestone 2 Report — The Rule System and Its Editor

Prompt 2.8, the Milestone 2 check. Covers the spec review, a project-rules sweep, a performance benchmark, save/load under load, the Milestone 1 carried-forward items, and decisions made during Milestone 2 that the design doc doesn't record.

## 1. Spec review

Checked against "Milestone 2 specs: the rule system and its editor" in `Docs/Design/05-TechnicalFramework.pdf`.

### Decisions

- **Our own editor on UI Toolkit** — done as written. No Graph Toolkit/`GraphView` usage anywhere; `RuleCanvasView` hand-rolls pan/zoom/wires.
- **Blueprint-style rules** — done as written. `PortKind.Execution` vs `PortKind.Data`; `RuleFieldValueSource` is literal or setting-reference.
- **Blocks are C#, registered by modules** — done differently. Blocks are plain C#, but they are registered by a direct static call, `CoreBlocks.RegisterAll(BlockRegistry)`, called from `SimulationBuilder.Build` and `EditorContext.Build` *before* the module declaration phase runs — not through any module's `IModuleDeclaration.Declare`. `ModuleDeclarationContext.Blocks` exists for a future module to register through, but nothing uses it yet; what's registered today is core-only. "Data mods combine existing blocks but can't create new kinds" holds.
- **Paused rules are saved and resume exactly on time** — done as written. `RuleRuntimeSection` persists paused instances (position, locals, triggering event); `Simulation.Step` resumes them before any other event delivery that tick.
- **Questions queue and pause the game by default** — the queue, the setting (`setting.rules.questions_pause_game`, default on) and the delegation hook (`IQuestionDelegate`) are done as written. The *presentation-layer* half ("pause the game") — actually halting clock advancement while a question is open — isn't wired up anywhere yet; there's no player-facing surface for questions at all yet, so this is a reasonable gap, not a bug.
- **Rule memory** — done as written: `RuleInstance.Locals` for running locals, `MemoryStore`/`MemorySection` for saved flags/counters.
- **Limits: 1,000 fires/rule/tick, chains capped at 10, loop warnings in the editor, switched off and reported in Release** — the two numeric limits are exact (`setting.rules.max_fires_per_tick` = 1000, `RuleRunner.MaxChainDepth` = 10). "Loop warnings in the editor" are compile-time warnings (unreachable nodes, self-triggering events), not a separate runtime loop detector — a reasonable reading. One real deviation: hitting the fire cap or chain-depth cap only skips *that fire* and logs a report — it does **not** switch the rule off. Only a runtime *exception* switches a rule off in Release mode. A rule that hits its fire cap every tick forever will just keep quietly skipping forever rather than disabling itself — worth a decision from William on whether that's the intended behavior.
- **In-game editor and system-specific blocks come later** — correctly out of scope.

### 1–9

1. **Rule data** — done as written (content type `"rule"`, node/port/connection/data-type shapes all match exactly), with one addition not in the doc: data ports can be **wildcard/untyped** (`PortDefinition.DataType` nullable), used by `PostEventActionBlockType`'s `param1`–`param3` inputs; the compiler's type-check treats a wildcard target as accepting any data type.
2. **Blocks** — done as written for the declared shape and pure-value semantics. "Modules register block types during the declaration phase" is the same gap noted above (mechanism unused).
3. **Compiling** — done as written. All five listed error kinds present, plus two more the doc doesn't list (unknown port Ids, duplicate node Ids) — additions, not deviations. Both listed warnings present. Development/Release error handling matches exactly.
4. **Running** — done as written in full: the four trigger types, instance creation and run-to-completion/wait/ask, next-tick event delivery, the rules' own named random stream (lazily resolved so a rules-free save stays byte-identical to one with no `RuleRunner`), and chain-depth tracking.
5. **Waiting, flags and counters** — done as written. Could not confirm from static reading alone whether `MemoryStore` is surfaced through the entity inspector ("shown by the inspector") — flagged, not claimed either way.
6. **Asking the player** — done as written, with one taxonomy note: `AskPlayerBlockType.Kind` is `BlockKind.Flow` (multiple named execution outputs), not `Action` as the doc's blocks table implies.
7. **Core blocks table** — done, with small additions beyond the doc's list: `SetCounterBlockType` exists alongside "get and change counter" (a direct overwrite, not just additive change); `Get Local`/`Set Local`/`Read Event Parameter` are each **one block per `RuleDataType`** (5 variants each) rather than one generic block, while `Math` is one block with a field-selected operator — two different patterns for "one block, several forms," worth stating explicitly somewhere.
8. **The editor** — done as written, plus one addition (Ctrl+D duplicate-selection) not in the doc.
9. **Testing and tracing** — done as written and then substantially exceeded. The Test tab's sandbox is built through the exact same `SimulationBuilder` pipeline the real game uses (via a one-parameter `contentOverlay` seam), well beyond "a sample trigger"; canvas highlighting shows execution order, followed wires and port values, not just pass/fail; and 2.7b added a full plain-language, numbered-step run view the doc never anticipated. Live tracing likewise has a full editor panel (last 50 segments, paused/pending counts) beyond "console commands... show its recent runs."

### Prompts table (2.1–2.8)

2.1 through 2.7 match their one-line doc descriptions. 2.6b, 2.6c and 2.7b (hand-testing fix-up passes) aren't reflected in the doc's prompt table, which only lists one row per whole-number prompt — normal iteration, not a gap.

**2.8 itself does not match.** The doc's row reads: *"Milestone check: a reaction to the demo hunger module rebuilt as a rule, authored in the editor, plus a rule William builds himself."* The actual 2.8 prompt is the broader milestone check this report covers — spec review, rules sweep, performance, save/load under load, carried-forward items, decisions audit, this report. Noted as a fact, not a bug: the doc's own prompt table was drafted before the actual closing prompt was decided, and "docs stay the source of truth, updated when a prompt shows a spec was wrong or incomplete" is exactly the process for this kind of drift.

## 2. Rules-of-the-project sweep

**Files with more than one public type, split (behavior unchanged, only code moved):**

| File | Had | Now |
| --- | --- | --- |
| `Code/Core/Rules/RuleTriggerDescriptor.cs` | `RuleTriggerDescriptor`, `EventTriggerDescriptor`, `RuleScheduleKind`, `ScheduleTriggerDescriptor` | split into 4 files |
| `Code/Core/Rules/Tracing/RuleTraceEntry.cs` | `RuleTraceEntryKind`, `RuleTraceEntry` | split into 2 files |
| `Code/Core/Rules/Tracing/RuleTraceSegment.cs` | `RuleTraceStartKind`, `RuleTraceEndKind`, `RuleTraceSegment` | split into 3 files |
| `Code/Editor/Rules/Canvas/UI/RuleCanvasView.cs` | `AddNodeMenuRequest`, `RuleCanvasView` | split into 2 files |
| `Code/Editor/Rules/Editing/AddNodeMenuModel.cs` | `AddNodeMenuEntry`, `AddNodeMenuModel` | split into 2 files |
| `Code/Editor/Rules/Editing/RuleEditorModel.cs` | `VariantSwapResult`, `ConnectionCheckResult`, `RuleEditorModel` | split into 3 files |

`Code/Editor/Rules/Editing/RuleContentSource.cs` holds only `RuleContentSource` (`RuleFileHandle` already has its own file) — that listed "known case" no longer applies.

Files with one public type plus small `internal` helper types in the same file (`RuleExecutionResult.cs`'s internal result subclasses, `RuleFieldValueSource.cs`'s internal JSON converter) were left as-is: exactly one *public* type, file name matches it.

**Other checks, all clean, no changes needed:**
- **Namespaces match folders** — every file under `Core/Rules`, `Core/Rules/CoreBlocks`, `Core/Rules/Tracing`, `Editor/Rules/*` declares the namespace its folder implies.
- **No `UnityEngine` in Core** — only two hits in all of `Code/Core/`, both harmless: a doc-comment in `ILogSink.cs` that mentions the word "UnityEngine" in prose, and `Core/README.md`. No actual `UnityEngine` reference in any Core code.
- **Ordinal string comparison** — every `OrderBy`/`Sort`/set lookup on a string in `Core/Rules` and `Editor/Rules` already uses `StringComparer.Ordinal` or `StringComparison.Ordinal` (`RuleCompiler`, `RuleIdValidator`, `RuleFileRepository`, `RuleJsonSerializer`, `NodeElement`, `BlockFamilyCatalog`, `EditorContext`). No `.ToLower()`/`.ToUpper()` (culture-sensitive) anywhere; slugging uses `.ToLowerInvariant()`.
- **Every test seeds its randomness** — every `new Simulation(...)`, `RuleTestSession.Build(...)` and `RuleTestPanel`'s seed field uses an explicit literal seed across every EditMode/PlayMode test file.

## 3. Performance

Benchmark: 2,000 entities, 500 rules (300 on events across 300 distinct event Ids — one rule each — with a sliding 50-event window posted each tick so about 50 events fire per tick; 200 on schedules split across every-N-hours/at-an-hour/on-new-day), 10 rotating rule templates together covering every core block kind (triggers, every value block, branch/wait/sequence/stop, get/set flag, get/change/set counter, local variables, post event/log line/herald line, ask the player). Run for one full in-game day (14,400 ticks). New test: `Tests/EditMode/Rules/RulePerformanceBenchmarkTests.cs` (`[Explicit]`, run by name for numbers — doesn't slow the normal suite) and its shared fixture `RuleBenchmarkFixture.cs`.

**Results:**

| Metric | Value |
| --- | --- |
| Startup (content load + compile + activate 500 rules) | 245.6 ms |
| Compiling 500 rules alone (content already loaded) | 14.6 ms |
| Whole simulation, average | 0.33 ms/tick |
| Whole simulation, worst | 15.4 ms/tick |
| Rule work alone, average | 0.33 ms/tick |
| Rule work alone, worst | 15.3 ms/tick |
| Allocations/tick (tracing off) | not reliably measurable — see below |

**Targets:** average (< 1 ms) **met**, comfortably (0.33 ms, less than a third of budget). Worst case (< 5 ms) **missed** — one tick cost 15.3 ms, roughly three times the budget.

**Where the worst-case time likely goes (not optimized, per the prompt):** across 14,400 ticks only one single tick is the outlier; every other tick's rule work stayed close to the 0.33 ms average (consistent with the average meeting target so comfortably). The far likelier of two straightforward causes: a first-tick cost (JIT warm-up of the ~500 compiled rules' `Execute`/`ComputeValue` paths, the rules random stream's lazy first resolution, and every `Dictionary`/`List` used by the runner growing from empty for the first time) rather than a sustained per-tick cost from the rule work itself — the same "a single outlier" pattern the Milestone 1 report recorded for its own benchmark (worst 4.5 ms there, also one outlier against a ~0.04 ms average). This wasn't instrumented further since the prompt asks only to report where the time goes, not to fix it.

**Allocations:** `GC.GetAllocatedBytesForCurrentThread()` reported exactly 0 bytes/tick over the whole run, which is almost certainly the API not tracking allocations reliably in this Editor/Mono test environment rather than a true zero — hundreds of thousands of rule steps, each boxing `RuleValue`s and touching per-step dictionaries, cannot plausibly allocate nothing. Reporting this as **not reliably measurable here**, not as a real number; a proper measurement needs the Unity/Memory Profiler or a build where this API is known to work, not an EditMode batch test.

## 4. Save and load under load

New test: `Tests/EditMode/Rules/RuleSaveLoadUnderLoadTests.cs`, using the same 500-rule/2,000-entity fixture (whose Wait and Ask Player rules keep paused instances, pending questions and outstanding timeouts in flight throughout the day by construction). Saves at three points in the day (roughly 1/7, 1/2 and 5/6 of the way through), confirms at each point at least one paused instance or pending question exists, loads each save (confirming the load resumes at the exact saved tick), runs each to the end of the day, and compares both the final state hash and the full log line history against an unbroken 14,400-tick run with the same seed. **All three save points match exactly** — state hashes and event logs are identical to the unbroken run.

## 5. Carried-forward items from Milestone 1

- **Per-tick allocations from table iteration, iterator wrappers and closures** — still open, not re-measured properly in this pass (the attempted measurement in section 3 wasn't reliable in this environment either). Carries forward again to a dedicated profiling pass.
- **Console and HUD built in C# instead of UXML/USS layout files** — unchanged; still true for the built-in console. The rule editor itself (a different, later piece of UI) is built on UI Toolkt with C#-constructed `VisualElement` trees rather than `.uxml`/`.uss` files too, for the same reason the console is — it needs to be built and iterated on quickly by Claude Code. Still an open item for when real game-facing screens are built.
- **Obsolete Unity API warnings** — searched for and **fixed, because none were found.** Checked every EditMode/PlayMode compile log (zero `warning CS0618`/`CS0612` entries, zero "obsolete" or "deprecated" mentions attributable to our own code — the only "Deprecated" hits are inside Unity's own `com.unity.render-pipelines.core` package, not ours) and grepped `Code/` directly for the usual deprecated-API patterns (`FindObjectOfType`, old `FindObjectsOfType` overloads, etc.) — none present. This item appears to have already been resolved somewhere along the way during Milestone 2's many prompts; nothing remained to fix here.
- **The temporary demo hunger module is deleted in Milestone 3** — not yet done (correctly out of scope for this prompt; `Code/Modules/DemoHunger/` and its content files are still present and still used by the rule editor's test tool — `RuleTestPanel`'s "Build" button, and multiple EditMode tests, go through `ModuleCatalog.CreateModules()`, which currently returns just `DemoHungerModule`). Flagging so Milestone 3's removal remembers these touch points.

## 6. Decisions made during Milestone 2 not in the design doc

- **Wire beats field when both are present.** `RuleStepContext.GetInput` checks a wire first, falls back to the field only if unwired (`Code/Core/Rules/RuleStepContext.cs`); the editor greys out a field's control once it's wired, matching this at edit time.
- **Question timeouts resolve to a default choice.** `AskPlayerBlockType`'s `timeoutHours`/`defaultChoice` fields; `RuleRunner.ProcessQuestionAnswers` resolves any question past its `TimeoutTick` using `DefaultChoiceNumber`.
- **Delegates answer in registration order.** `RuleRunner._questionDelegates`, tried in list order inside `TryResolveImmediatelyOrEnqueue`; first valid answer wins.
- **One block per data type for Get Local / Set Local / Read Event Parameter**, each baking its `RuleDataType` into its own Id and display name (`Code/Core/Rules/CoreBlocks/CoreBlocks.cs`'s per-type registration loop) — a deliberate choice distinct from Math's single block with a field-selected operator; worth stating explicitly since either pattern is reasonable and a future contributor could assume the wrong one.
- **Wildcard (untyped) data ports.** `PortDefinition.DataType` is nullable; `null` means "accepts any data type," used by Post Event's `param1`–`param3` inputs.
- **The step cap counts per run segment, not per rule lifetime.** `RuleRunner.MaxStepsPerInstance` is checked against a fresh counter every `RunFrom` call (one trigger/resume/answer to end/wait/ask/cap/error) — a rule that waits and resumes many times over a long game can take far more than 10,000 total steps across its lifetime without ever hitting the cap.
- **Resumed instances and answered questions run before calendar events and the scheduler, every tick.** `Simulation.Step`'s own order: resume paused instances and process answers, *then* post `NewHour`/`NewDay`/etc., *then* run the scheduler, *then* deliver events.
- **Stable, byte-for-byte-preserving JSON saving for rule files.** Fixed key order, nodes/connections never resorted, field keys in ascending ordinal order, and — specifically — an untouched field's literal value is re-emitted exactly as it was read, not reformatted, so an unedited number or string's original representation survives a load-then-save round trip unchanged (`RuleJsonSerializer`).
- **Node Id numbering never reuses a deleted node's Id.** New node Ids are `"n<max existing number + 1>"`, scanned fresh each time — not a running counter, not a reused gap.
- **The rule editor keeps its own undo/redo history, separate from Unity's own `Undo` system** (`RuleEditorModel`'s private snapshot stack) — deliberate, since the same model needs to work identically if the canvas ever ships inside the running game, where `UnityEditor.Undo` doesn't exist.
- **Tracing is attached per rule, not globally.** `RuleRunner._traceListeners` is keyed by rule Id; tracing one rule in a 500-rule game costs nothing for the other 499.
- **The test tool's sandbox is built through one `contentOverlay` seam on `SimulationBuilder.Build`**, not a parallel pipeline: it clones the live (possibly unsaved) rule via a JSON round-trip so the editor's own object is never mutated, forces it enabled, optionally disables every other loaded rule for "isolated" mode, and otherwise goes through the exact same startup the real game uses.
- **`BuiltInConsoleCommands.RegisterAll`'s `ILogSink` parameter falls back to the presentation object if not given one explicitly** — kept the `trace` console command from needing to change ~10 existing call sites.
- **`AskPlayerBlockType` is categorized as Flow, not Action**, because — like Branch and Sequence — it has several named execution outputs instead of one "then".
- **Rules are JSON-only content, never ScriptableObjects**, even in core content, despite the doc's earlier (pre-Milestone-2) general claim that every content type is "a ScriptableObject in the editor and a JSON file for mods." This is a doc-internal inconsistency between that earlier section and the rule system's own later section, not a deviation from the rule system's own spec.

## Test counts

- EditMode: 334 passing, 1 explicit (the performance benchmark, run separately by name).
- PlayMode: 17 passing.
- Save/load under load (3 save points × 1 full-day run each, plus one unbroken reference day): state hashes and full event/log-line histories match exactly at every point.

## What's worth fixing or deciding before Milestone 3

1. **Decide: should a rule that keeps hitting its fire cap every tick eventually switch itself off?** Right now it just quietly skips forever (see Decisions/Limits above). This is a design call, not a bug — flagging for William.
2. **"Questions pause the game" isn't wired up anywhere yet.** No bug today (nothing player-facing exists to pause), but worth remembering once a real play surface exists.
3. **Block registration bypasses the module declaration phase.** `ModuleDeclarationContext.Blocks` exists but nothing uses it; `CoreBlocks.RegisterAll` is called directly instead. Not broken, but if a future module ever needs to register its own blocks, this is the seam that needs actually wiring up, not just existing.
4. **The worst-case tick (15.3 ms, section 3) wasn't root-caused**, only plausibly attributed to first-tick warm-up. Worth a short, dedicated look before any milestone where the game actually ships with hundreds of active rules at once.
5. **Per-tick allocation measurement needs a real profiler**, not `GC.GetAllocatedBytesForCurrentThread()` in an EditMode batch test — carried forward from Milestone 1 again, now confirmed the same simple approach still doesn't work here.
6. **`Code/Modules/DemoHunger/`'s removal (planned for Milestone 3) now has more touch points to clean up**: the rule editor's Test tab hardcodes `ModuleCatalog.CreateModules()` for its "Build" button, and several EditMode tests go through the same call — worth a quick grep pass when that module is actually deleted.
