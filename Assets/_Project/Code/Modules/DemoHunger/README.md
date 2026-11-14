# DemoHunger — TEMPORARY

This module exists only to prove the milestone-1 core framework end to end:
modules, scheduling with importance tiers, entities and module data tables,
the event bus, data-driven settings and event definitions, saves, the
console and the inspector.

It is **not** a real system. It will be deleted in milestone 3, once real
people and a real needs system exist.

Scope: on a new game it creates 50 `Person` entities with hunger 100, lowers
hunger over time using `BucketedWork` and importance tiers, posts an
`event.demo_hunger.hunger_changed` event (entity, hunger, previousHunger) on
every update, and exposes `feed <entityId>` plus the usual save/inspect
support.

The milestone-2 check (prompt 2.8b) moved the starving reaction itself out of
this module and into a core rule: `Content/core/Data/demo_hunger_starving_reaction.json`
listens for `hunger_changed`, compares the old and new values to the same
threshold (20) the module used to check in C#, and posts
`event.demo_hunger.starving` when hunger crosses below it. That rule (and the
`event.demo_hunger.starving`/`event.demo_hunger.hunger_changed` definitions in
`Content/core/Data/demo_hunger_events.json`) is core content tied to this
temporary module's events, so delete it alongside this module in milestone 3.
