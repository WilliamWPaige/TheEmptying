using System;
using System.Collections.Generic;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Core.Modules
{
    /// <summary>
    /// Gives a module access to the simulation's services. Every system and event
    /// subscription registered through this context is tracked and tagged as
    /// belonging to the module, so UnregisterAll can remove all of it if the
    /// module is switched off. Scheduled systems and event handlers registered
    /// here are wrapped so a throw inside them is routed through the host
    /// (onFailure) instead of escaping raw into the Scheduler or EventBus.
    /// </summary>
    public sealed class ModuleContext
    {
        private readonly Action<string, string, Exception> _onFailure;
        private readonly List<string> _registeredSystemIds = new List<string>();
        private readonly List<EventSubscriptionToken> _subscriptionTokens = new List<EventSubscriptionToken>();
        private readonly List<string> _registeredCommandNames = new List<string>();

        public string ModuleId { get; }
        public EventBus Events { get; }
        public Scheduler Scheduler { get; }
        public EntityRegistry Entities { get; }
        public EventDefinitionRegistry EventDefinitions { get; }
        public SimRandom Random { get; }
        public ConsoleCommandRegistry Commands { get; }

        /// <summary>Current setting values (declared from data or by a module). Read-only here; settings change through the "set" console command.</summary>
        public SettingsRegistry Settings { get; }

        /// <summary>True when this Initialize is part of a load, not a new game. Modules must not run new-game setup when this is true.</summary>
        public bool IsLoading { get; }

        internal ModuleContext(
            string moduleId,
            EventBus events,
            Scheduler scheduler,
            EntityRegistry entities,
            EventDefinitionRegistry eventDefinitions,
            RandomService randomService,
            ConsoleCommandRegistry commands,
            SettingsRegistry settings,
            Action<string, string, Exception> onFailure,
            bool isLoading)
        {
            ModuleId = moduleId;
            Events = events;
            Scheduler = scheduler;
            Entities = entities;
            EventDefinitions = eventDefinitions;
            Random = randomService.GetStream(moduleId);
            Commands = commands;
            Settings = settings;
            _onFailure = onFailure;
            IsLoading = isLoading;
        }

        public void RegisterSystem(ISimSystem system, long interval, long offset, int order)
        {
            var guarded = new GuardedSystem(system, ModuleId, _onFailure);
            Scheduler.Register(guarded, interval, offset, order);
            _registeredSystemIds.Add(guarded.Id);
        }

        /// <summary>Registers a console command owned by this module; removed automatically if the module is switched off.</summary>
        public void RegisterCommand(ConsoleCommand command)
        {
            Commands.Register(command);
            _registeredCommandNames.Add(command.Name);
        }

        public EventSubscriptionToken Subscribe<T>(Action<T> handler) where T : struct, ISimEvent
        {
            void Guarded(T e)
            {
                try
                {
                    handler(e);
                }
                catch (Exception ex)
                {
                    _onFailure(ModuleId, "event handler", ex);
                }
            }

            EventSubscriptionToken token = Events.Subscribe<T>(Guarded);
            _subscriptionTokens.Add(token);
            return token;
        }

        public EventSubscriptionToken Subscribe(string definitionId, Action<PostedEvent> handler)
        {
            void Guarded(PostedEvent e)
            {
                try
                {
                    handler(e);
                }
                catch (Exception ex)
                {
                    _onFailure(ModuleId, "event handler", ex);
                }
            }

            EventSubscriptionToken token = Events.Subscribe(definitionId, Guarded);
            _subscriptionTokens.Add(token);
            return token;
        }

        internal void UnregisterAll()
        {
            foreach (string systemId in _registeredSystemIds)
            {
                Scheduler.Unregister(systemId);
            }

            _registeredSystemIds.Clear();

            foreach (EventSubscriptionToken token in _subscriptionTokens)
            {
                token.Unsubscribe();
            }

            _subscriptionTokens.Clear();

            foreach (string commandName in _registeredCommandNames)
            {
                Commands.Unregister(commandName);
            }

            _registeredCommandNames.Clear();
        }

        private sealed class GuardedSystem : ISimSystem
        {
            private readonly ISimSystem _inner;
            private readonly string _moduleId;
            private readonly Action<string, string, Exception> _onFailure;

            public GuardedSystem(ISimSystem inner, string moduleId, Action<string, string, Exception> onFailure)
            {
                _inner = inner;
                _moduleId = moduleId;
                _onFailure = onFailure;
            }

            public string Id => _inner.Id;

            public void Update(long tick)
            {
                try
                {
                    _inner.Update(tick);
                }
                catch (Exception ex)
                {
                    _onFailure(_moduleId, "system update", ex);
                }
            }
        }
    }
}
