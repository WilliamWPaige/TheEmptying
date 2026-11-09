using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Core.Startup;
using TheEmptying.Editor.Rules.Editing;

namespace TheEmptying.Editor.Rules.Testing
{
    /// <summary>
    /// The rule editor's Test panel: a throwaway Simulation built the same
    /// way the game builds (SimulationBuilder, same content sources and
    /// module list), with the rule currently open in the editor overlaid on
    /// top - including unsaved changes - and traced for the whole session.
    /// Never touches files or a running game. One session per "Build" call;
    /// Clear rebuilds a fresh one with the same settings.
    /// </summary>
    public sealed class RuleTestSession
    {
        private sealed class CollectingListener : IRuleTraceListener
        {
            private readonly RuleTestSession _owner;
            public CollectingListener(RuleTestSession owner) => _owner = owner;
            public void OnSegmentCompleted(RuleTraceSegment segment) => _owner._segments.Add(segment);
        }

        private readonly List<RuleTraceSegment> _segments = new List<RuleTraceSegment>();

        private RuleDefinition _liveRule;
        private IReadOnlyList<string> _sourceFolders;
        private IReadOnlyList<IModule> _modules;
        private ErrorMode _errorMode;

        public Simulation Simulation { get; private set; }
        public RuleRunner Runner => Simulation?.RuleRunner;
        public string RuleId { get; private set; }
        public bool Isolated { get; private set; }
        public ulong Seed { get; private set; }
        public StartupReport LastStartupReport { get; private set; }

        public IReadOnlyList<RuleTraceSegment> Segments => _segments;

        public bool IsBuilt => Simulation != null;

        /// <summary>Builds (or rebuilds) the throwaway simulation with liveRule overlaid on the on-disk content loaded from sourceFolders.</summary>
        public void Build(RuleDefinition liveRule, IReadOnlyList<string> sourceFolders, IEnumerable<IModule> modules, bool isolated, ulong seed, ErrorMode errorMode = ErrorMode.Development)
        {
            _liveRule = liveRule ?? throw new ArgumentNullException(nameof(liveRule));
            _sourceFolders = sourceFolders ?? Array.Empty<string>();
            _modules = modules?.ToList() ?? new List<IModule>();
            _errorMode = errorMode;
            RuleId = liveRule.Id;
            Isolated = isolated;
            Seed = seed;

            RebuildInternal();
        }

        /// <summary>Resets the throwaway simulation: rebuilds it fresh (same rule, isolation and seed) and clears the recorded trace.</summary>
        public void Clear()
        {
            if (_liveRule == null)
            {
                return;
            }

            RebuildInternal();
        }

        private void RebuildInternal()
        {
            _segments.Clear();

            RuleDefinition overlay = CloneForOverlay(_liveRule);

            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(
                Seed, _sourceFolders, _modules, _errorMode,
                contentOverlay: db => ApplyOverlay(db, overlay, Isolated));

            Simulation = simulation;
            LastStartupReport = report;

            Simulation.RuleRunner?.SetTraceListener(overlay.Id, new CollectingListener(this));
        }

        /// <summary>Fires an On Event trigger directly (not through a full tick), with the given parameters as event data.</summary>
        public void FireEvent(string eventDefinitionId, IReadOnlyList<(string Name, RuleDataType Type, object Value)> parameters)
        {
            RequireBuilt();
            EventData data = BuildEventData(parameters);
            Simulation.Events.Post(eventDefinitionId, Simulation.Clock.CurrentTick, data);
            Simulation.Events.DeliverPending();
        }

        /// <summary>Fires a time trigger right now, bypassing its schedule.</summary>
        public void FireNow()
        {
            RequireBuilt();
            Runner?.FireNow(RuleId);
        }

        /// <summary>Whether this rule currently has a paused (waiting) instance, and if so, the tick it will resume at.</summary>
        public bool TryGetResumeTick(out long resumeTick)
        {
            resumeTick = 0;
            if (Runner == null)
            {
                return false;
            }

            foreach ((string ruleId, long tick, RuleInstance _) in Runner.PausedInstances)
            {
                if (ruleId == RuleId)
                {
                    resumeTick = tick;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Steps the throwaway simulation forward exactly to this rule's resume tick, if it has a paused instance.</summary>
        public void SkipToResume()
        {
            RequireBuilt();
            if (!TryGetResumeTick(out long resumeTick))
            {
                return;
            }

            while (Simulation.Clock.CurrentTick < resumeTick)
            {
                Simulation.Step();
            }
        }

        /// <summary>Every question currently pending for this rule.</summary>
        public IReadOnlyList<QuestionInfo> PendingQuestionsForRule()
        {
            if (Runner == null)
            {
                return Array.Empty<QuestionInfo>();
            }

            return Runner.PendingQuestions.Where(q => q.RuleId == RuleId).ToList();
        }

        /// <summary>Answers a pending question; applied once the next tick runs (same as the real game).</summary>
        public void AnswerQuestion(long questionId, int choiceNumber)
        {
            RequireBuilt();
            Runner?.QueueAnswer(questionId, choiceNumber);
            Simulation.Step();
        }

        private void RequireBuilt()
        {
            if (Simulation == null)
            {
                throw new InvalidOperationException("RuleTestSession.Build must be called before firing, skipping or answering.");
            }
        }

        private static EventData BuildEventData(IReadOnlyList<(string Name, RuleDataType Type, object Value)> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            var data = new EventData();
            foreach ((string name, RuleDataType type, object value) in parameters)
            {
                switch (type)
                {
                    case RuleDataType.Number:
                        data.SetDouble(name, Convert.ToDouble(value));
                        break;
                    case RuleDataType.Bool:
                        data.SetBool(name, Convert.ToBoolean(value));
                        break;
                    case RuleDataType.Text:
                    case RuleDataType.ContentId:
                        data.SetString(name, value?.ToString() ?? string.Empty);
                        break;
                    case RuleDataType.Entity:
                        if (TheEmptying.Core.Entities.EntityId.TryParse(value?.ToString(), out TheEmptying.Core.Entities.EntityId id))
                        {
                            data.SetEntity(name, id);
                        }

                        break;
                }
            }

            return data;
        }

        /// <summary>A safe-to-mutate copy of the live (possibly unsaved) rule, forced enabled regardless of its own checkbox, so the test tool always runs the rule it's showing.</summary>
        private static RuleDefinition CloneForOverlay(RuleDefinition liveRule)
        {
            string json = RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { liveRule });
            RuleDefinition clone = RuleJsonSerializer.DeserializeFile(json)[0];
            clone.Enabled = true;
            return clone;
        }

        /// <summary>Isolated: every OTHER loaded rule is disabled before compiling, so only overlayRule can ever fire. Either way, overlayRule itself replaces whatever on-disk version of it exists (or is simply added, if it's new and unsaved).</summary>
        private static void ApplyOverlay(ContentDatabase database, RuleDefinition overlayRule, bool isolated)
        {
            if (isolated)
            {
                foreach (ContentEntry entry in database.ListByType(BuiltInContentTypes.RuleTypeName).ToList())
                {
                    if (entry.Definition.Id != overlayRule.Id && entry.Definition is RuleDefinition otherRule)
                    {
                        otherRule.Enabled = false;
                    }
                }
            }

            database.Set(BuiltInContentTypes.RuleTypeName, overlayRule, "test-tool", "in-memory");
        }
    }
}
