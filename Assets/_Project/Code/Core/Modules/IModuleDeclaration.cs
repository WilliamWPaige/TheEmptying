using TheEmptying.Core.Content;

namespace TheEmptying.Core.Modules
{
    /// <summary>
    /// Optional. A module implements this to register content types and declare
    /// settings before content loads. Simple modules that don't need this stay
    /// plain IModule implementations.
    /// </summary>
    public interface IModuleDeclaration
    {
        void Declare(ModuleDeclarationContext context);
    }
}
