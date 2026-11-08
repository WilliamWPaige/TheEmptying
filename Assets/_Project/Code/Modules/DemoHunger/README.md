# DemoHunger — TEMPORARY

This module exists only to prove the milestone-1 core framework end to end:
modules, scheduling with importance tiers, entities and module data tables,
the event bus, data-driven settings and event definitions, saves, the
console and the inspector.

It is **not** a real system. It will be deleted in milestone 3, once real
people and a real needs system exist.

Scope: on a new game it creates 50 `Person` entities with hunger 100, lowers
hunger over time using `BucketedWork` and importance tiers, posts a
`event.demo_hunger.starving` event when an entity crosses below 20 hunger,
and exposes `feed <entityId>` plus the usual save/inspect support.
