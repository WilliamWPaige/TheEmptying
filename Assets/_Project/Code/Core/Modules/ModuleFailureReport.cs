using System;

namespace TheEmptying.Core.Modules
{
    /// <summary>Recorded in Release ErrorMode when a module is switched off because it threw.</summary>
    public sealed class ModuleFailureReport
    {
        public string ModuleId { get; }
        public string Call { get; }
        public Exception Error { get; }
        public long Tick { get; }

        public ModuleFailureReport(string moduleId, string call, Exception error, long tick)
        {
            ModuleId = moduleId;
            Call = call;
            Error = error;
            Tick = tick;
        }
    }
}
