using System;

namespace TheEmptying.Core.Modules
{
    /// <summary>A missing dependency, a circular dependency, or a duplicate module ID found during ModuleHost.Setup.</summary>
    public sealed class ModuleValidationException : Exception
    {
        public ModuleValidationException(string message) : base(message)
        {
        }
    }
}
