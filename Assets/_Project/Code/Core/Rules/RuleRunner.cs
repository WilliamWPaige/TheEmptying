using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Activates every compiled, enabled rule (event triggers subscribe through
    /// the event bus; time triggers register with the scheduler), and runs
    /// instances when they fire. Created and activated by SimulationBuilder,
    /// after modules initialize.
    /// </summary>
    public sealed class RuleRunner
    {
        public const string MaxFiresPerTickSettingId = "setting.rules.max_fires_per_tick";
        public const int MaxChainDepth = 10;
        internal const int MaxStepsPerInstance = 10000;

        private sealed class NullLogSink : ILogSink
        {
            public static readonly NullLogSink Instance = new NullLogSink();
            public void Log(string message)
            {
            }
        }

        private sealed class ScheduleTriggerSystem : ISimSystem
        {
            private readonly Action _onFire;
            public string Id { get; }

            public ScheduleTriggerSystem(string ruleId, Action onFire)
            {
                Id = $"rule.{ruleId}.trigger";
                _onFire = onFire;
            }

            public void Update(long tick) => _onFire();
        }

        private readonly RuleRegistry _registry;
        private readonly EventBus _events;
        private readonly Scheduler _scheduler;
        private readonly EventDefinitionRegistry _eventDefinitions;
        private readonly ILogSink _logSink;
        private readonly ErrorMode _errorMode;
        private readonly Func<long> _currentTick;

        private readonly Dictionary<string, ActivatedRule> _activated = new Dictionary<string, ActivatedRule>();
        private readonly List<RuleRuntimeReport> _reports = new List<RuleRuntimeReport>();

        private readonly Dictionary<string, int> _firesThisTick = new Dictionary<string, int>();
        private long _firesTickStamp = long.MinValue;

        private readonly RandomService _randomService;
        private SimRandom _randomStream;

        /// <summary>
        /// The "rules" random stream, resolved lazily on first use. A
        /// simulation whose rules never roll anything must never register
        /// this stream at all, so its save data stays byte-identical to one
        /// built without a RuleRunner.
        /// </summary>
        internal SimRandom RandomStream => _randomStream ??= _randomService.GetStream("rules");

        internal SettingsRegistry Settings { get; }

        public IReadOnlyList<RuleRuntimeReport> Reports => _reports;

        public RuleRunner(
            RuleRegistry registry,
            EventBus events,
            Scheduler scheduler,
            RandomService randomService,
            SettingsRegistry settings,
            EventDefinitionRegistry eventDefinitions,
            ILogSink logSink,
            ErrorMode errorMode,
            Func<long> currentTick)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _randomService = randomService ?? throw new ArgumentNullException(nameof(randomService));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _eventDefinitions = eventDefinitions ?? throw new ArgumentNullException(nameof(eventDefinitions));
            _logSink = logSink ?? NullLogSink.Instance;
            _errorMode = errorMode;
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        }

        /// <summary>Activates every compiled, enabled rule in ordinal rule Id order.</summary>
        public void Activate()
        {
            foreach (CompiledRule rule in _registry.All)
            {
                ActivateRule(rule);
            }
        }

        public bool IsRuleActive(string ruleId)
        {
            return _activated.TryGetValue(ruleId, out ActivatedRule activated) && activated.IsActive;
        }

        private void ActivateRule(CompiledRule rule)
        {
            CompiledRuleNode triggerNode = rule.Nodes[rule.TriggerNodeId];
            RuleTriggerDescriptor descriptor = triggerNode.BlockType.DescribeTrigger(triggerNode.Fields, Settings);
            if (descriptor == null)
            {
                return;
            }

            var activated = new ActivatedRule(rule) { Trigger = descriptor };
            BuildConnectionIndexes(activated);

            switch (descriptor)
            {
                case EventTriggerDescriptor eventTrigger:
                    if (string.IsNullOrEmpty(eventTrigger.EventDefinitionId) || !_eventDefinitions.IsRegistered(eventTrigger.EventDefinitionId))
                    {
                        Report(rule.Id, rule.TriggerNodeId, $"Trigger resolves to event Id '{eventTrigger.EventDefinitionId}', which is not registered; the rule was not activated.", _currentTick());
                        return;
                    }

                    EventSubscriptionToken token = _events.Subscribe(eventTrigger.EventDefinitionId, posted => OnEventTriggered(activated, posted));
                    activated.Deactivate = () => token.Unsubscribe();
                    break;

                case ScheduleTriggerDescriptor schedule:
                    ActivateSchedule(activated, schedule);
                    break;
            }

            _activated[rule.Id] = activated;
        }

        private void ActivateSchedule(ActivatedRule activated, ScheduleTriggerDescriptor schedule)
        {
            var system = new ScheduleTriggerSystem(activated.Rule.Id, () => OnScheduleTriggered(activated));

            long interval;
            long offset;

            switch (schedule.ScheduleKind)
            {
                case RuleScheduleKind.EveryNHours:
                    interval = Math.Max(1, schedule.Hours) * CalendarConstants.TicksPerHour;
                    offset = 0;
                    break;

                case RuleScheduleKind.AtHourOfDay:
                    interval = CalendarConstants.TicksPerDay;
                    offset = Math.Clamp(schedule.HourOfDay, 0, CalendarConstants.HoursPerDay - 1) * CalendarConstants.TicksPerHour;
                    break;

                default: // OnNewDay
                    interval = CalendarConstants.TicksPerDay;
                    offset = 0;
                    break;
            }

            _scheduler.Register(system, interval, offset, 0);
            activated.Deactivate = () => _scheduler.Unregister(system.Id);
        }

        private static void BuildConnectionIndexes(ActivatedRule activated)
        {
            foreach (CompiledRuleConnection connection in activated.Rule.Connections)
            {
                CompiledRuleNode fromNode = activated.Rule.Nodes[connection.FromNode];
                PortDefinition fromPort = fromNode.BlockType.Ports[connection.FromPortIndex];

                if (fromPort.Kind == PortKind.Execution)
                {
                    activated.ExecOutputTargets[(connection.FromNode, fromPort.Name)] = connection.ToNode;
                }
                else
                {
                    CompiledRuleNode toNode = activated.Rule.Nodes[connection.ToNode];
                    PortDefinition toPort = toNode.BlockType.Ports[connection.ToPortIndex];
                    activated.DataInputConnections[(connection.ToNode, toPort.Name)] = connection;
                }
            }

            foreach (KeyValuePair<(string Node, string Port), string> kvp in activated.ExecOutputTargets)
            {
                if (kvp.Key.Node == activated.Rule.TriggerNodeId)
                {
                    activated.TriggerNextNodeId = kvp.Value;
                    break;
                }
            }
        }

        private void OnEventTriggered(ActivatedRule activated, PostedEvent posted)
        {
            if (!activated.IsActive)
            {
                return;
            }

            long tick = _currentTick();

            if (posted.ChainDepth >= MaxChainDepth)
            {
                Report(activated.Rule.Id, activated.Rule.TriggerNodeId, $"Triggering event chain depth {posted.ChainDepth} is at or beyond the limit ({MaxChainDepth}); the rule did not run.", tick);
                return;
            }

            if (!TryReserveFireSlot(activated.Rule.Id, tick))
            {
                return;
            }

            var instance = new RuleInstance(activated.Rule.Id, posted, posted.ChainDepth + 1);
            RunInstanceGuarded(instance, activated, tick);
        }

        private void OnScheduleTriggered(ActivatedRule activated)
        {
            if (!activated.IsActive)
            {
                return;
            }

            long tick = _currentTick();

            if (!TryReserveFireSlot(activated.Rule.Id, tick))
            {
                return;
            }

            var instance = new RuleInstance(activated.Rule.Id, null, 0);
            RunInstanceGuarded(instance, activated, tick);
        }

        private bool TryReserveFireSlot(string ruleId, long tick)
        {
            if (_firesTickStamp != tick)
            {
                _firesThisTick.Clear();
                _firesTickStamp = tick;
            }

            int maxFires = (int)Settings.Get(MaxFiresPerTickSettingId);
            _firesThisTick.TryGetValue(ruleId, out int count);

            if (count >= maxFires)
            {
                if (count == maxFires)
                {
                    Report(ruleId, null, $"Rule '{ruleId}' reached its per-tick fire cap ({maxFires}); further fires this tick are skipped.", tick);
                }

                _firesThisTick[ruleId] = count + 1;
                return false;
            }

            _firesThisTick[ruleId] = count + 1;
            return true;
        }

        private void RunInstanceGuarded(RuleInstance instance, ActivatedRule activated, long tick)
        {
            try
            {
                var steps = new RuleStepsState();
                RunFrom(activated.TriggerNextNodeId, instance, activated, steps);
            }
            catch (RuleNodeExecutionException ex)
            {
                HandleRuleFailure(activated, ex.NodeId, ex.InnerException ?? ex, tick);
            }
            catch (Exception ex)
            {
                HandleRuleFailure(activated, activated.Rule.TriggerNodeId, ex, tick);
            }
        }

        /// <summary>Walks execution wires from startNodeId to completion. Shared across RunExecutionOutput sub-runs via the same RuleStepsState, so the step cap applies to the whole instance.</summary>
        internal void RunFrom(string startNodeId, RuleInstance instance, ActivatedRule activated, RuleStepsState steps)
        {
            string currentNodeId = startNodeId;

            while (currentNodeId != null)
            {
                if (steps.Count >= MaxStepsPerInstance)
                {
                    if (!steps.CapReported)
                    {
                        steps.CapReported = true;
                        Report(activated.Rule.Id, currentNodeId, $"Rule '{activated.Rule.Id}' exceeded the maximum of {MaxStepsPerInstance} steps in one run; it was stopped.", _currentTick());
                    }

                    return;
                }

                steps.Count++;

                CompiledRuleNode node = activated.Rule.Nodes[currentNodeId];
                var context = new RuleStepContext(this, instance, activated, currentNodeId, steps);

                string outputPort;
                try
                {
                    outputPort = node.BlockType.Execute(context);
                }
                catch (RuleNodeExecutionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new RuleNodeExecutionException(currentNodeId, ex);
                }

                string nextNodeId = null;
                if (outputPort != null)
                {
                    activated.ExecOutputTargets.TryGetValue((currentNodeId, outputPort), out nextNodeId);
                }

                currentNodeId = nextNodeId;
            }
        }

        internal void Log(string message) => _logSink.Log(message);

        internal void PostRuleEvent(string eventDefinitionId, IReadOnlyList<(string Name, RuleValue Value)> parameters, RuleInstance instance)
        {
            if (string.IsNullOrEmpty(eventDefinitionId) || !_eventDefinitions.IsRegistered(eventDefinitionId))
            {
                throw new InvalidOperationException($"Cannot post event with unregistered definition Id '{eventDefinitionId}'.");
            }

            EventData data = null;
            if (parameters != null && parameters.Count > 0)
            {
                data = new EventData();
                foreach ((string name, RuleValue value) in parameters)
                {
                    switch (value.DataType)
                    {
                        case RuleDataType.Number:
                            data.SetDouble(name, value.AsNumber);
                            break;
                        case RuleDataType.Bool:
                            data.SetBool(name, value.AsBool);
                            break;
                        case RuleDataType.Text:
                        case RuleDataType.ContentId:
                            data.SetString(name, value.AsText);
                            break;
                        case RuleDataType.Entity:
                            data.SetEntity(name, value.AsEntity);
                            break;
                    }
                }
            }

            _events.Post(eventDefinitionId, _currentTick(), data, instance.PostedEventChainDepth);
        }

        private void HandleRuleFailure(ActivatedRule activated, string nodeId, Exception error, long tick)
        {
            string message = $"Rule '{activated.Rule.Id}' node '{nodeId}' failed: {error.Message}";

            if (_errorMode == ErrorMode.Development)
            {
                throw new RuleException(message, activated.Rule.Id, nodeId, error);
            }

            _reports.Add(new RuleRuntimeReport(activated.Rule.Id, nodeId, message, tick));
            DeactivateRule(activated);
        }

        private void DeactivateRule(ActivatedRule activated)
        {
            if (!activated.IsActive)
            {
                return;
            }

            activated.IsActive = false;
            activated.Deactivate?.Invoke();
        }

        private void Report(string ruleId, string nodeId, string message, long tick)
        {
            _reports.Add(new RuleRuntimeReport(ruleId, nodeId, message, tick));
        }
    }
}
