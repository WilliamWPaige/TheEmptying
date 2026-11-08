using TheEmptying.Core.Content;
using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Modules
{
    public sealed class ModuleDeclarationContext
    {
        public string ModuleId { get; }
        public ContentTypeRegistry ContentTypes { get; }
        public SettingsRegistry Settings { get; }
        public BlockRegistry Blocks { get; }

        internal ModuleDeclarationContext(string moduleId, ContentTypeRegistry contentTypes, SettingsRegistry settings, BlockRegistry blocks)
        {
            ModuleId = moduleId;
            ContentTypes = contentTypes;
            Settings = settings;
            Blocks = blocks;
        }
    }
}
