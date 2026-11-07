using System;

namespace TheEmptying.Core.Modules
{
    /// <summary>Thrown in Development ErrorMode when a module throws during Initialize, Shutdown, a scheduled system update or an event handler.</summary>
    public sealed class ModuleException : Exception
    {
        public string ModuleId { get; }
        public string Call { get; }

        public ModuleException(string message, string moduleId, string call, Exception innerException)
            : base(message, innerException)
        {
            ModuleId = moduleId;
            Call = call;
        }
    }
}
