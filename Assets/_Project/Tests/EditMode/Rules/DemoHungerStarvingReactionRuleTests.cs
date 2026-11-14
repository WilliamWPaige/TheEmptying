using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.DemoHunger;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>
    /// Prompt 2.8b (the milestone 2 check): the demo hunger module's starving
    /// reaction was rebuilt as a core rule (rule.demo_hunger.starving_reaction
    /// in Content/core/Data/demo_hunger_starving_reaction.json), listening for
    /// the module's new event.demo_hunger.hunger_changed instead of the
    /// module posting event.demo_hunger.starving itself in C#.
    /// </summary>
    public class DemoHungerStarvingReactionRuleTests
    {
        [Test]
        public void HungerCrossingBelowThreshold_PostsAStarvingEventForThatEntity_ViaTheRule()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { new DemoHungerModule() });

            sim.Settings.Set(DemoHungerModule.DecayPerHourSettingId, 20);

            var starving = new List<EntityId>();
            sim.Events.Subscribe(DemoHungerModule.StarvingEventId, posted => starving.Add(posted.Data.GetEntity("entity")));

            for (int i = 0; i < 3000 && starving.Count == 0; i++)
            {
                sim.Step();
            }

            Assert.Greater(starving.Count, 0, "Expected at least one starving event with a high decay setting.");
            Assert.IsTrue(sim.Entities.IsAlive(starving[0]));
        }

        [Test]
        public void FeedingAStarvingEntity_LetsItStarveAgainLater()
        {
            // No persistent "already starving" flag is used (the rule compares the event's
            // own previousHunger/hunger parameters), so an entity fed back to full hunger
            // can cross the threshold and starve again later, exactly as the original
            // oldHunger/newHunger check in C# would have allowed.
            using var core = new TempCoreContentCopy();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { new DemoHungerModule() });

            sim.Settings.Set(DemoHungerModule.DecayPerHourSettingId, 20);

            EntityId first = sim.Entities.LivingEntities().First();
            var starvingTicksForFirst = new List<long>();
            sim.Events.Subscribe(DemoHungerModule.StarvingEventId, posted =>
            {
                if (posted.Data.GetEntity("entity") == first)
                {
                    starvingTicksForFirst.Add(posted.Tick);
                }
            });

            for (int i = 0; i < 3000 && starvingTicksForFirst.Count == 0; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(1, starvingTicksForFirst.Count, "Should have starved exactly once so far.");

            sim.Commands.Execute($"feed {first}");

            for (int i = 0; i < 3000 && starvingTicksForFirst.Count < 2; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(2, starvingTicksForFirst.Count, "Should be able to starve a second time after being fed.");
        }

        /// <summary>
        /// A characterization test against the behavior before prompt 2.8b: the exact
        /// (tick, entityIndex) pairs below were captured from the pre-refactor code, where
        /// DemoHungerModule.UpdateHunger itself checked "oldHunger &gt;= 20 &amp;&amp; newHunger &lt; 20"
        /// in C# and posted event.demo_hunger.starving directly, for world seed 777 and the
        /// default settings, over exactly two in-game days. The jitter in hunger decay means
        /// some entities cross the threshold more than once in that window - that's expected,
        /// matching the original per-tick check having no debounce either.
        /// </summary>
        [Test]
        public void TwoDayRun_WithDefaultSettings_MatchesTheExactEventsFromBeforeTheRuleRefactor()
        {
            var expected = new (long Tick, long EntityIndex)[]
            {
                (20961, 21), (20981, 21), (21121, 21), (21510, 30), (21640, 30), (21740, 30), (21760, 30),
                (21854, 24), (21914, 24), (21964, 24), (21994, 24), (22073, 13), (22074, 24), (22144, 24),
                (22167, 7), (22264, 24), (22534, 34), (22782, 22), (22803, 3), (22843, 3), (23052, 12),
                (23079, 19), (23099, 39), (23105, 15), (23122, 12), (23211, 31), (23286, 36), (23297, 37),
                (23378, 48), (23498, 38), (23500, 20), (23509, 29), (23517, 17), (23525, 5), (23583, 33),
                (23603, 33), (23623, 33), (23653, 33), (23666, 26), (23668, 28), (23801, 1), (23842, 2),
                (23905, 25), (23964, 44), (24008, 8), (24132, 32), (24154, 14), (24276, 16), (24295, 35),
                (24440, 40), (24601, 41), (24686, 46), (24783, 23), (24791, 11), (24830, 10), (24872, 42),
                (24919, 9), (24927, 47), (25284, 4), (25320, 0), (25350, 0), (25448, 18), (25468, 18),
                (25523, 43), (25569, 49), (27096, 6), (27116, 6), (28047, 27), (28097, 27), (28575, 45)
            };

            using var core = new TempCoreContentCopy();
            const ulong seed = 777;
            (Simulation sim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, new IModule[] { new DemoHungerModule() });

            var actual = new List<(long Tick, long EntityIndex)>();
            sim.Events.Subscribe(DemoHungerModule.StarvingEventId, posted => actual.Add((posted.Tick, posted.Data.GetEntity("entity").Index)));

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                sim.Step();
            }

            CollectionAssert.AreEqual(expected, actual, "The reaction now runs as a rule, but it must still post the same starving events at the same ticks as the C# version it replaced.");
        }
    }
}
