using System.Runtime.CompilerServices;

// Tests build Simulation objects by hand (bypassing SimulationBuilder) for
// fine control over rule compilation and activation, and need to wire a
// RuleRunner into Simulation.RuleRunner (internal set) to do it.
[assembly: InternalsVisibleTo("TheEmptying.Tests.EditMode")]
[assembly: InternalsVisibleTo("TheEmptying.Tests.PlayMode")]

// Editor tooling (the rule canvas) needs to build a BlockRegistry the same
// way SimulationBuilder does - construct a Simulation and run its
// declaration phase only, with no content loaded and no modules
// initialized - which needs the internal Simulation(seed, errorMode) ctor.
[assembly: InternalsVisibleTo("TheEmptying.Editor")]
