using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Core.Modules
{
    /// <summary>
    /// Validates, orders, initializes and shuts down a set of modules, and is the
    /// single place every call into module code passes through (Initialize,
    /// Shutdown, scheduled system updates, event handlers), so failures can be
    /// handled per ErrorMode.
    /// </summary>
    public sealed class ModuleHost
    {
        private readonly EventBus _events;
        private readonly Scheduler _scheduler;
        private readonly EntityRegistry _entities;
        private readonly EventDefinitionRegistry _eventDefinitions;
        private readonly RandomService _random;
        private readonly Func<long> _currentTick;

        private readonly Dictionary<string, IModule> _modules = new Dictionary<string, IModule>();
        private readonly Dictionary<string, ModuleManifest> _manifests = new Dictionary<string, ModuleManifest>();
        private readonly Dictionary<string, List<string>> _dependents = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, ModuleContext> _contexts = new Dictionary<string, ModuleContext>();

        private readonly List<string> _initializationOrder = new List<string>();
        private readonly HashSet<string> _activeModuleIds = new HashSet<string>();
        private readonly List<string> _disabledReasons = new List<string>();
        private readonly List<ModuleFailureReport> _failureReports = new List<ModuleFailureReport>();

        private List<string> _order;
        private bool _declared;

        public ErrorMode ErrorMode { get; set; }

        public IReadOnlyList<string> InitializationOrder => _initializationOrder;
        public IReadOnlyList<string> DisabledReasons => _disabledReasons;
        public IReadOnlyList<ModuleFailureReport> FailureReports => _failureReports;
        public IReadOnlyCollection<string> RegisteredModuleIds => _modules.Keys;

        public ModuleHost(
            EventBus events,
            Scheduler scheduler,
            EntityRegistry entities,
            EventDefinitionRegistry eventDefinitions,
            RandomService random,
            Func<long> currentTick,
            ErrorMode errorMode = ErrorMode.Development)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            _eventDefinitions = eventDefinitions ?? throw new ArgumentNullException(nameof(eventDefinitions));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
            ErrorMode = errorMode;
        }

        public bool IsActive(string moduleId) => _activeModuleIds.Contains(moduleId);

        /// <summary>Convenience for callers that don't need content loaded between declaration and initialization.</summary>
        public void Setup(IEnumerable<ModuleRegistration> registrations)
        {
            ValidateAndOrder(registrations);
            RunDeclarationPhase(new ContentTypeRegistry(), new SettingsRegistry());
            InitializeModules();
        }

        /// <summary>Phase 1: validate manifests (including against each other) and apply disabling, then compute dependency order.</summary>
        public void ValidateAndOrder(IEnumerable<ModuleRegistration> registrations)
        {
            List<ModuleRegistration> list = registrations?.ToList() ?? new List<ModuleRegistration>();

            RegisterAll(list);
            ValidateManifestsDeclareModuleDependencies();
            ValidateDependenciesExist();
            ValidateNoCycles();

            HashSet<string> enabledIds = ApplyDisabling();
            _order = ComputeOrder(enabledIds);

            foreach (string id in _order)
            {
                _activeModuleIds.Add(id);
            }
        }

        /// <summary>Phase 2: each enabled module, in dependency order, can register content types and declare settings.</summary>
        public void RunDeclarationPhase(ContentTypeRegistry contentTypes, SettingsRegistry settings)
        {
            if (_order == null)
            {
                throw new InvalidOperationException("RunDeclarationPhase called before ValidateAndOrder.");
            }

            if (_declared)
            {
                throw new InvalidOperationException("RunDeclarationPhase already ran.");
            }

            _declared = true;

            foreach (string id in _order)
            {
                if (!_activeModuleIds.Contains(id))
                {
                    continue;
                }

                if (!(_modules[id] is IModuleDeclaration declarable))
                {
                    continue;
                }

                var context = new ModuleDeclarationContext(id, contentTypes, settings);

                try
                {
                    declarable.Declare(context);
                }
                catch (Exception ex)
                {
                    HandleModuleFailure(id, "declare", ex);
                }
            }
        }

        /// <summary>Phase 4: initializes enabled modules in dependency order. Call after content has loaded (phase 3, owned by the caller).</summary>
        public void InitializeModules()
        {
            if (_order == null)
            {
                throw new InvalidOperationException("InitializeModules called before ValidateAndOrder.");
            }

            foreach (string id in _order)
            {
                if (!_activeModuleIds.Contains(id))
                {
                    continue;
                }

                var context = new ModuleContext(id, _events, _scheduler, _entities, _eventDefinitions, _random, HandleModuleFailure);
                _contexts[id] = context;

                try
                {
                    _modules[id].Initialize(context);
                    _initializationOrder.Add(id);
                }
                catch (Exception ex)
                {
                    HandleModuleFailure(id, "initialize", ex);
                }
            }
        }

        public void Shutdown()
        {
            for (int i = _initializationOrder.Count - 1; i >= 0; i--)
            {
                string id = _initializationOrder[i];
                if (!_activeModuleIds.Contains(id))
                {
                    continue;
                }

                try
                {
                    _modules[id].Shutdown();
                }
                catch (Exception ex)
                {
                    HandleModuleFailure(id, "shutdown", ex);
                }
            }
        }

        private void RegisterAll(List<ModuleRegistration> registrations)
        {
            foreach (ModuleRegistration reg in registrations)
            {
                if (reg.Module == null)
                {
                    throw new ArgumentException("A module registration has a null Module.");
                }

                if (reg.Manifest == null)
                {
                    throw new ArgumentException("A module registration has a null Manifest.");
                }

                string id = reg.Module.Id;
                if (reg.Manifest.Id != id)
                {
                    throw new ModuleValidationException($"Module '{id}' and its manifest ('{reg.Manifest.Id}') disagree on Id.");
                }

                if (_modules.ContainsKey(id))
                {
                    throw new ModuleValidationException($"Duplicate module Id '{id}'.");
                }

                _modules[id] = reg.Module;
                _manifests[id] = reg.Manifest;
            }

            foreach (ModuleManifest manifest in _manifests.Values)
            {
                foreach (string dep in manifest.Dependencies)
                {
                    if (!_dependents.TryGetValue(dep, out List<string> dependentsOfDep))
                    {
                        dependentsOfDep = new List<string>();
                        _dependents[dep] = dependentsOfDep;
                    }

                    dependentsOfDep.Add(manifest.Id);
                }
            }
        }

        private void ValidateManifestsDeclareModuleDependencies()
        {
            foreach (KeyValuePair<string, IModule> kvp in _modules)
            {
                string id = kvp.Key;
                IModule module = kvp.Value;
                ModuleManifest manifest = _manifests[id];

                foreach (string dep in module.Dependencies)
                {
                    if (!manifest.Dependencies.Contains(dep))
                    {
                        throw new ModuleValidationException($"Module '{id}' declares a dependency on '{dep}' that its manifest does not list.");
                    }
                }
            }
        }

        private void ValidateDependenciesExist()
        {
            foreach (ModuleManifest manifest in _manifests.Values)
            {
                foreach (string dep in manifest.Dependencies)
                {
                    if (!_manifests.ContainsKey(dep))
                    {
                        throw new ModuleValidationException($"Module '{manifest.Id}' depends on '{dep}', which is not registered.");
                    }
                }
            }
        }

        private void ValidateNoCycles()
        {
            var state = new Dictionary<string, int>();

            foreach (string id in _manifests.Keys)
            {
                if (!state.TryGetValue(id, out int s) || s == 0)
                {
                    VisitForCycle(id, state, new List<string>());
                }
            }
        }

        private void VisitForCycle(string id, Dictionary<string, int> state, List<string> path)
        {
            state[id] = 1;
            path.Add(id);

            foreach (string dep in _manifests[id].Dependencies)
            {
                if (!state.TryGetValue(dep, out int depState) || depState == 0)
                {
                    VisitForCycle(dep, state, path);
                }
                else if (depState == 1)
                {
                    int cycleStart = path.IndexOf(dep);
                    List<string> cycle = path.GetRange(cycleStart, path.Count - cycleStart);
                    cycle.Add(dep);
                    throw new ModuleValidationException($"Circular module dependency: {string.Join(" -> ", cycle)}.");
                }
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }

        private HashSet<string> ApplyDisabling()
        {
            var disabled = new HashSet<string>();
            var queue = new Queue<string>();

            foreach (ModuleManifest manifest in _manifests.Values)
            {
                if (!manifest.Enabled && disabled.Add(manifest.Id))
                {
                    _disabledReasons.Add($"Module '{manifest.Id}' is disabled.");
                    queue.Enqueue(manifest.Id);
                }
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!_dependents.TryGetValue(current, out List<string> deps))
                {
                    continue;
                }

                foreach (string dependent in deps)
                {
                    if (disabled.Add(dependent))
                    {
                        _disabledReasons.Add($"Module '{dependent}' disabled because its dependency '{current}' is disabled.");
                        queue.Enqueue(dependent);
                    }
                }
            }

            var enabled = new HashSet<string>(_manifests.Keys);
            enabled.ExceptWith(disabled);
            return enabled;
        }

        private List<string> ComputeOrder(HashSet<string> enabledIds)
        {
            var remainingDeps = new Dictionary<string, int>();
            foreach (string id in enabledIds)
            {
                remainingDeps[id] = _manifests[id].Dependencies.Count;
            }

            var available = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string id in enabledIds)
            {
                if (remainingDeps[id] == 0)
                {
                    available.Add(id);
                }
            }

            var order = new List<string>();
            while (available.Count > 0)
            {
                string next = available.Min;
                available.Remove(next);
                order.Add(next);

                if (_dependents.TryGetValue(next, out List<string> deps))
                {
                    foreach (string dependent in deps)
                    {
                        if (!enabledIds.Contains(dependent))
                        {
                            continue;
                        }

                        remainingDeps[dependent]--;
                        if (remainingDeps[dependent] == 0)
                        {
                            available.Add(dependent);
                        }
                    }
                }
            }

            if (order.Count != enabledIds.Count)
            {
                throw new ModuleValidationException("Circular dependency detected among enabled modules.");
            }

            return order;
        }

        private void HandleModuleFailure(string moduleId, string call, Exception exception)
        {
            if (ErrorMode == ErrorMode.Development)
            {
                throw new ModuleException($"Module '{moduleId}' failed during {call}: {exception.Message}", moduleId, call, exception);
            }

            long tick = _currentTick();
            _failureReports.Add(new ModuleFailureReport(moduleId, call, exception, tick));

            DisableModuleCascade(moduleId, $"Module '{moduleId}' failed during {call} and was switched off.");
        }

        private void DisableModuleCascade(string rootId, string reason)
        {
            if (!_activeModuleIds.Remove(rootId))
            {
                return;
            }

            _disabledReasons.Add(reason);

            if (_contexts.TryGetValue(rootId, out ModuleContext rootContext))
            {
                rootContext.UnregisterAll();
            }

            var queue = new Queue<string>();
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!_dependents.TryGetValue(current, out List<string> deps))
                {
                    continue;
                }

                foreach (string dependent in deps)
                {
                    if (_activeModuleIds.Remove(dependent))
                    {
                        _disabledReasons.Add($"Module '{dependent}' disabled because its dependency '{current}' failed or was switched off.");

                        if (_contexts.TryGetValue(dependent, out ModuleContext depContext))
                        {
                            depContext.UnregisterAll();
                        }

                        queue.Enqueue(dependent);
                    }
                }
            }
        }
    }
}
