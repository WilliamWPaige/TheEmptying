using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Memory;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Activates every compiled, enabled rule (event triggers subscribe through
    /// the event bus; time triggers register with the scheduler), and runs
    /// instances when they fire. An instance's whole position (current node
    /// plus a continuation stack) is plain data on the RuleInstance itself, so
    /// a Wait can pause it anywhere and PauseInstance below can save it as-is.
    /// Created and activated by SimulationBuilder, after modules initialize.
    /// </summary>
    public sealed class RuleRunner
    {
        public const string MaxFiresPerTickSettingId = "setting.rules.max_fires_per_tick";
        public const string QuestionsPauseGameSettingId = "setting.rules.questions_pause_game";
        public const string QuestionAskedEventId = "event.question_asked";
        public const string QuestionAnsweredEventId = "event.question_answered";
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

        private sealed class PausedEntry
        {
            public string RuleId;
            public long ResumeTick;
            public RuleInstance Instance;
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
        private readonly List<PausedEntry> _paused = new List<PausedEntry>();

        private readonly List<PendingQuestion> _pendingQuestions = new List<PendingQuestion>();
        private readonly List<IQuestionDelegate> _questionDelegates = new List<IQuestionDelegate>();
        private readonly List<(long QuestionId, int ChoiceNumber)> _queuedAnswers = new List<(long, int)>();
        private long _nextQuestionId = 1;

        private readonly Dictionary<string, int> _firesThisTick = new Dictionary<string, int>();
        private long _firesTickStamp = long.MinValue;
        private long _nextInstanceId = 1;

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
        internal MemoryStore Memory { get; }
        internal long CurrentTick => _currentTick();

        public IReadOnlyList<RuleRuntimeReport> Reports => _reports;

        /// <summary>For saving: the instance Id counter.</summary>
        internal long NextInstanceId => _nextInstanceId;

        /// <summary>For saving: every currently paused instance, in instance Id order.</summary>
        internal IEnumerable<(string RuleId, long ResumeTick, RuleInstance Instance)> PausedInstances =>
            _paused.OrderBy(p => p.Instance.InstanceId).Select(p => (p.RuleId, p.ResumeTick, p.Instance));

        /// <summary>Every pending question, oldest first, for the console and the presentation popup.</summary>
        public IReadOnlyList<QuestionInfo> PendingQuestions => _pendingQuestions.OrderBy(q => q.QuestionId).Select(q => q.ToInfo()).ToList();

        /// <summary>For saving: the question Id counter.</summary>
        internal long NextQuestionId => _nextQuestionId;

        /// <summary>For saving: every pending question, in question Id order.</summary>
        internal IEnumerable<PendingQuestion> PendingQuestionsForSave => _pendingQuestions.OrderBy(q => q.QuestionId);

        /// <summary>Registers a delegate that may answer questions on the player's behalf. Tried in registration order.</summary>
        public void RegisterQuestionDelegate(IQuestionDelegate questionDelegate)
        {
            _questionDelegates.Add(questionDelegate ?? throw new ArgumentNullException(nameof(questionDelegate)));
        }

        public RuleRunner(
            RuleRegistry registry,
            EventBus events,
            Scheduler scheduler,
            RandomService randomService,
            SettingsRegistry settings,
            EventDefinitionRegistry eventDefinitions,
            MemoryStore memory,
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
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
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

            var instance = new RuleInstance(_nextInstanceId++, activated.Rule.Id, posted, posted.ChainDepth + 1);
            instance.Position.CurrentNodeId = activated.TriggerNextNodeId;
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

            var instance = new RuleInstance(_nextInstanceId++, activated.Rule.Id, null, 0);
            instance.Position.CurrentNodeId = activated.TriggerNextNodeId;
            RunInstanceGuarded(instance, activated, tick);
        }

        /// <summary>
        /// Resumes every instance due this tick or earlier, in instance Id
        /// order, before scheduled systems run. Called by Simulation.Step at
        /// the start of each tick.
        /// </summary>
        public void ResumeDueInstances(long tick)
        {
            if (_paused.Count == 0)
            {
                return;
            }

            List<PausedEntry> due = _paused.Where(p => p.ResumeTick <= tick).OrderBy(p => p.Instance.InstanceId).ToList();

            foreach (PausedEntry entry in due)
            {
                _paused.Remove(entry);

                if (!_activated.TryGetValue(entry.RuleId, out ActivatedRule activated) || !activated.IsActive)
                {
                    Report(entry.RuleId, null, $"Instance {entry.Instance.InstanceId} was due to resume but rule '{entry.RuleId}' is switched off or no longer exists; it was dropped.", tick);
                    continue;
                }

                RunInstanceGuarded(entry.Instance, activated, tick);
            }
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
                RunFrom(instance, activated, steps);
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

        /// <summary>
        /// Runs instance.Position forward until it ends or waits. Pure loop,
        /// no recursion: a Sequence pushes a continuation frame instead of
        /// calling back in, so the whole position stays plain data the entire
        /// time and a Wait can snapshot it exactly as it stands.
        /// </summary>
        private void RunFrom(RuleInstance instance, ActivatedRule activated, RuleStepsState steps)
        {
            RuleExecutionPosition position = instance.Position;

            while (true)
            {
                if (position.CurrentNodeId == null)
                {
                    if (!TryAdvanceFromContinuation(position, activated))
                    {
                        return;
                    }

                    continue;
                }

                if (steps.Count >= MaxStepsPerInstance)
                {
                    if (!steps.CapReported)
                    {
                        steps.CapReported = true;
                        Report(activated.Rule.Id, position.CurrentNodeId, $"Rule '{activated.Rule.Id}' exceeded the maximum of {MaxStepsPerInstance} steps in one run; it was stopped.", _currentTick());
                    }

                    return;
                }

                steps.Count++;

                string nodeId = position.CurrentNodeId;
                position.CurrentNodeId = null;

                CompiledRuleNode node = activated.Rule.Nodes[nodeId];
                var context = new RuleStepContext(this, instance, activated, nodeId);

                RuleExecutionResult result;
                try
                {
                    result = node.BlockType.Execute(context);
                }
                catch (RuleNodeExecutionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new RuleNodeExecutionException(nodeId, ex);
                }

                switch (result)
                {
                    case FollowExecutionResult follow:
                        position.CurrentNodeId = ResolveNext(activated, nodeId, follow.PortName);
                        break;

                    case FollowInOrderExecutionResult followMany:
                        PushContinuation(position, nodeId, followMany.PortNames);
                        break;

                    case WaitExecutionResult wait:
                        position.CurrentNodeId = ResolveNext(activated, nodeId, wait.ResumePortName);
                        PauseInstance(instance, activated.Rule.Id, wait.ResumeTick);
                        return;

                    case AskExecutionResult ask:
                        // position.CurrentNodeId stays null: which output to resume on
                        // isn't known until the question is answered.
                        HandleAsk(instance, activated, nodeId, ask, _currentTick());
                        return;

                    default:
                        position.CurrentNodeId = null;
                        break;
                }
            }
        }

        private static bool TryAdvanceFromContinuation(RuleExecutionPosition position, ActivatedRule activated)
        {
            while (position.ContinuationStack.Count > 0)
            {
                RuleContinuationFrame frame = position.ContinuationStack[position.ContinuationStack.Count - 1];
                if (frame.RemainingPorts.Count == 0)
                {
                    position.ContinuationStack.RemoveAt(position.ContinuationStack.Count - 1);
                    continue;
                }

                string portName = frame.RemainingPorts[0];
                frame.RemainingPorts.RemoveAt(0);

                string nextNodeId = ResolveNext(activated, frame.NodeId, portName);
                if (nextNodeId != null)
                {
                    position.CurrentNodeId = nextNodeId;
                    return true;
                }
            }

            return false;
        }

        private static void PushContinuation(RuleExecutionPosition position, string nodeId, IReadOnlyList<string> portNames)
        {
            if (portNames == null || portNames.Count == 0)
            {
                return;
            }

            position.ContinuationStack.Add(new RuleContinuationFrame(nodeId, new List<string>(portNames)));
        }

        private static string ResolveNext(ActivatedRule activated, string fromNodeId, string portName)
        {
            if (portName == null)
            {
                return null;
            }

            return activated.ExecOutputTargets.TryGetValue((fromNodeId, portName), out string next) ? next : null;
        }

        private void PauseInstance(RuleInstance instance, string ruleId, long resumeTick)
        {
            _paused.Add(new PausedEntry { RuleId = ruleId, ResumeTick = resumeTick, Instance = instance });
        }

        /// <summary>For loading: restores the instance Id counter and every paused instance, dropping (and reporting) any whose rule or node no longer exists.</summary>
        internal void RestorePausedState(long nextInstanceId, IEnumerable<(string RuleId, long ResumeTick, RuleInstance Instance)> paused)
        {
            _nextInstanceId = nextInstanceId;
            _paused.Clear();

            foreach ((string ruleId, long resumeTick, RuleInstance instance) in paused)
            {
                if (!_activated.TryGetValue(ruleId, out ActivatedRule activated))
                {
                    Report(ruleId, null, $"Saved paused instance {instance.InstanceId} references rule '{ruleId}', which no longer exists; it was dropped.", resumeTick);
                    continue;
                }

                if (!PositionNodesExist(activated, instance.Position))
                {
                    Report(ruleId, null, $"Saved paused instance {instance.InstanceId}'s rule '{ruleId}' changed shape (one of its nodes no longer exists); it was dropped.", resumeTick);
                    continue;
                }

                _paused.Add(new PausedEntry { RuleId = ruleId, ResumeTick = resumeTick, Instance = instance });
            }
        }

        private static bool PositionNodesExist(ActivatedRule activated, RuleExecutionPosition position)
        {
            if (position.CurrentNodeId != null && !activated.Rule.Nodes.ContainsKey(position.CurrentNodeId))
            {
                return false;
            }

            foreach (RuleContinuationFrame frame in position.ContinuationStack)
            {
                if (!activated.Rule.Nodes.ContainsKey(frame.NodeId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Called once, at the start of each tick, before ResumeDueInstances:
        /// applies every queued player answer (in question Id order), then
        /// resolves any question whose timeout has run out, picking its
        /// default choice.
        /// </summary>
        public void ProcessQuestionAnswers(long tick)
        {
            if (_queuedAnswers.Count > 0)
            {
                List<(long QuestionId, int ChoiceNumber)> answers = _queuedAnswers.OrderBy(a => a.QuestionId).ToList();
                _queuedAnswers.Clear();

                foreach ((long questionId, int choiceNumber) in answers)
                {
                    PendingQuestion question = _pendingQuestions.FirstOrDefault(q => q.QuestionId == questionId);
                    if (question != null)
                    {
                        ResolveQuestion(question, choiceNumber, QuestionAnsweredBy.Player, tick);
                    }
                }
            }

            if (_pendingQuestions.Count == 0)
            {
                return;
            }

            List<PendingQuestion> timedOut = _pendingQuestions
                .Where(q => q.TimeoutTick > 0 && q.TimeoutTick <= tick)
                .OrderBy(q => q.QuestionId)
                .ToList();

            foreach (PendingQuestion question in timedOut)
            {
                ResolveQuestion(question, question.DefaultChoiceNumber, QuestionAnsweredBy.Timeout, tick);
            }
        }

        /// <summary>
        /// Queues a player's answer for ProcessQuestionAnswers to apply at the
        /// start of the next tick. Returns a rejection message (and queues
        /// nothing) if the question doesn't exist or the choice is empty.
        /// </summary>
        public string QueueAnswer(long questionId, int choiceNumber)
        {
            PendingQuestion question = _pendingQuestions.FirstOrDefault(q => q.QuestionId == questionId);
            if (question == null)
            {
                return $"Question {questionId} does not exist (it may already have been answered or withdrawn).";
            }

            if (choiceNumber < 1 || choiceNumber > 4 || string.IsNullOrEmpty(question.ChoiceTexts[choiceNumber - 1]))
            {
                return $"Choice {choiceNumber} is empty for question {questionId}.";
            }

            _queuedAnswers.Add((questionId, choiceNumber));
            return null;
        }

        private void HandleAsk(RuleInstance instance, ActivatedRule activated, string nodeId, AskExecutionResult ask, long tick)
        {
            PendingQuestion question = AskQuestion(activated.Rule.Id, nodeId, instance, ask.Category, ask.Title, ask.Body, ask.ChoiceTexts, ask.DefaultChoiceNumber, ask.TimeoutTicks, tick);
            TryResolveImmediatelyOrEnqueue(question, tick);
        }

        /// <summary>Asks a question not tied to any rule instance - for modules that need to ask the player something outside the rule system.</summary>
        public long AskQuestion(string category, string title, string body, string[] choiceTexts, int defaultChoiceNumber, double timeoutHours = 0)
        {
            long tick = _currentTick();
            long timeoutTicks = timeoutHours > 0 ? (long)Math.Round(timeoutHours * CalendarConstants.TicksPerHour, MidpointRounding.AwayFromZero) : 0;

            PendingQuestion question = AskQuestion(null, null, null, category, title, body, choiceTexts, defaultChoiceNumber, timeoutTicks, tick);
            TryResolveImmediatelyOrEnqueue(question, tick);
            return question.QuestionId;
        }

        private PendingQuestion AskQuestion(string ruleId, string askingNodeId, RuleInstance instance, string category, string title, string body, string[] choiceTexts, int defaultChoiceNumber, long timeoutTicks, long tick)
        {
            return new PendingQuestion
            {
                QuestionId = _nextQuestionId++,
                RuleId = ruleId,
                AskingNodeId = askingNodeId,
                Instance = instance,
                Category = category ?? string.Empty,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                ChoiceTexts = (string[])(choiceTexts ?? new string[4]).Clone(),
                DefaultChoiceNumber = defaultChoiceNumber,
                TimeoutTick = timeoutTicks > 0 ? tick + timeoutTicks : 0,
                TickAsked = tick
            };
        }

        /// <summary>Posts "event.question_asked", then gives each registered delegate (in registration order) a chance to answer before the question reaches the player.</summary>
        private void TryResolveImmediatelyOrEnqueue(PendingQuestion question, long tick)
        {
            PostQuestionAskedEvent(question, tick);

            foreach (IQuestionDelegate questionDelegate in _questionDelegates)
            {
                if (questionDelegate.TryAnswer(question.ToInfo(), out int choiceNumber) &&
                    choiceNumber >= 1 && choiceNumber <= 4 &&
                    !string.IsNullOrEmpty(question.ChoiceTexts[choiceNumber - 1]))
                {
                    ResolveQuestion(question, choiceNumber, QuestionAnsweredBy.Delegate, tick);
                    return;
                }
            }

            _pendingQuestions.Add(question);
        }

        private void ResolveQuestion(PendingQuestion question, int choiceNumber, QuestionAnsweredBy answeredBy, long tick)
        {
            _pendingQuestions.Remove(question);
            PostQuestionAnsweredEvent(question, choiceNumber, answeredBy, tick);

            if (question.Instance == null)
            {
                return;
            }

            if (!_activated.TryGetValue(question.RuleId, out ActivatedRule activated) || !activated.IsActive)
            {
                Report(question.RuleId, question.AskingNodeId, $"Question {question.QuestionId} was answered but rule '{question.RuleId}' is switched off or no longer exists; the instance was dropped.", tick);
                return;
            }

            question.Instance.Position.CurrentNodeId = ResolveNext(activated, question.AskingNodeId, $"choice{choiceNumber}");
            RunInstanceGuarded(question.Instance, activated, tick);
        }

        private void PostQuestionAskedEvent(PendingQuestion question, long tick)
        {
            if (!_eventDefinitions.IsRegistered(QuestionAskedEventId))
            {
                return;
            }

            EventData data = new EventData()
                .SetDouble("questionId", question.QuestionId)
                .SetString("ruleId", question.RuleId ?? string.Empty)
                .SetString("category", question.Category ?? string.Empty);

            _events.Post(QuestionAskedEventId, tick, data);
        }

        private void PostQuestionAnsweredEvent(PendingQuestion question, int choiceNumber, QuestionAnsweredBy answeredBy, long tick)
        {
            if (!_eventDefinitions.IsRegistered(QuestionAnsweredEventId))
            {
                return;
            }

            EventData data = new EventData()
                .SetDouble("questionId", question.QuestionId)
                .SetString("ruleId", question.RuleId ?? string.Empty)
                .SetString("category", question.Category ?? string.Empty)
                .SetDouble("choice", choiceNumber)
                .SetString("answeredBy", answeredBy.ToString());

            _events.Post(QuestionAnsweredEventId, tick, data);
        }

        /// <summary>Withdraws (and reports) every pending question belonging to a rule that was just switched off.</summary>
        private void WithdrawQuestionsForRule(string ruleId, long tick)
        {
            List<PendingQuestion> toWithdraw = _pendingQuestions.Where(q => q.RuleId == ruleId).OrderBy(q => q.QuestionId).ToList();
            foreach (PendingQuestion question in toWithdraw)
            {
                _pendingQuestions.Remove(question);
                Report(ruleId, question.AskingNodeId, $"Question {question.QuestionId} was withdrawn because rule '{ruleId}' was switched off.", tick);
            }
        }

        /// <summary>For loading: restores the question Id counter and every pending question, dropping (and reporting) any whose rule or node no longer exists.</summary>
        internal void RestoreQuestions(long nextQuestionId, IEnumerable<PendingQuestion> questions)
        {
            _nextQuestionId = nextQuestionId;
            _pendingQuestions.Clear();

            foreach (PendingQuestion question in questions)
            {
                if (question.RuleId != null)
                {
                    if (!_activated.TryGetValue(question.RuleId, out ActivatedRule activated))
                    {
                        Report(question.RuleId, question.AskingNodeId, $"Saved pending question {question.QuestionId} references rule '{question.RuleId}', which no longer exists; it was withdrawn.", _currentTick());
                        continue;
                    }

                    if (question.AskingNodeId != null && !activated.Rule.Nodes.ContainsKey(question.AskingNodeId))
                    {
                        Report(question.RuleId, question.AskingNodeId, $"Saved pending question {question.QuestionId}'s rule '{question.RuleId}' changed shape (the asking node no longer exists); it was withdrawn.", _currentTick());
                        continue;
                    }

                    if (question.Instance != null && !PositionNodesExist(activated, question.Instance.Position))
                    {
                        Report(question.RuleId, question.AskingNodeId, $"Saved pending question {question.QuestionId}'s rule '{question.RuleId}' changed shape; it was withdrawn.", _currentTick());
                        continue;
                    }
                }

                _pendingQuestions.Add(question);
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
            WithdrawQuestionsForRule(activated.Rule.Id, _currentTick());
        }

        private void Report(string ruleId, string nodeId, string message, long tick)
        {
            _reports.Add(new RuleRuntimeReport(ruleId, nodeId, message, tick));
        }
    }
}
