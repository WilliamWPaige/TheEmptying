using TheEmptying.Core.Content;

namespace TheEmptying.Core.Modules
{
    public sealed class ModuleDeclarationContext
    {
        public string ModuleId { get; }
        public ContentTypeRegistry ContentTypes { get; }
        public SettingsRegistry Settings { get; }

        internal ModuleDeclarationContext(string moduleId, ContentTypeRegistry contentTypes, SettingsRegistry settings)
        {
            ModuleId = moduleId;
            ContentTypes = contentTypes;
            Settings = settings;
        }
    }
}
