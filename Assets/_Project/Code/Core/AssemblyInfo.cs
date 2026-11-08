using System.Runtime.CompilerServices;

// Tests build Simulation objects by hand (bypassing SimulationBuilder) for
// fine control over rule compilation and activation, and need to wire a
// RuleRunner into Simulation.RuleRunner (internal set) to do it.
[assembly: InternalsVisibleTo("TheEmptying.Tests.EditMode")]
[assembly: InternalsVisibleTo("TheEmptying.Tests.PlayMode")]
