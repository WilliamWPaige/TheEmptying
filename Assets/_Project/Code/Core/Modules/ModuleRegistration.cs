namespace TheEmptying.Core.Modules
{
    public readonly struct ModuleRegistration
    {
        public IModule Module { get; }
        public ModuleManifest Manifest { get; }

        public ModuleRegistration(IModule module, ModuleManifest manifest)
        {
            Module = module;
            Manifest = manifest;
        }
    }
}
