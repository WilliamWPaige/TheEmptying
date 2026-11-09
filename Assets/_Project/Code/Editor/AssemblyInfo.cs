using System.Runtime.CompilerServices;

// So EditMode tests can reach internal-but-structurally-important details (e.g. a
// NodeElement's internal containers) without making UI-only implementation details public.
[assembly: InternalsVisibleTo("TheEmptying.Tests.EditMode")]
