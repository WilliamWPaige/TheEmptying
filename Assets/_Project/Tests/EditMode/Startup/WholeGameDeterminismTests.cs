using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Presentation;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// Whole-game determinism, replacing the Milestone 1 test deleted with
    /// demo hunger in 3.4. Built on the real module list (ModuleCatalog) and
    /// the real shipped content, ScriptableObject content included, so every
    /// future module is covered automatically: nothing here names a specific
    /// module.
    /// </summary>
    public class WholeGameDeterminismTests
    {
        private static (Simulation Simulation, List<string> EventLog) Build(TempCoreContentCopy core, ulong seed, bool isLoading = false)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, isLoading: isLoading, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, CaptureEventLog(simulation));
        }

        private static (Simulation Simulation, List<string> EventLog) Load(Stream stream, TempCoreContentCopy core)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, LoadReport _) = GameSave.Load(stream, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, CaptureEventLog(simulation));
        }

        /// <summary>Subscribes to every registered event definition, so any module's events (present or future) are logged without naming one.</summary>
        private static List<string> CaptureEventLog(Simulation simulation)
        {
            var log = new List<string>();
            foreach (EventDefinition definition in simulation.EventDefinitions.All)
            {
                simulation.Events.Subscribe(definition.Id, posted => log.Add(Summarize(posted)));
            }

            return log;
        }

        private static string Summarize(PostedEvent posted)
        {
            var sb = new StringBuilder();
            sb.Append(posted.Tick).Append(':').Append(posted.DefinitionId);

            if (posted.Data != null)
            {
                AppendSorted(sb, "e", posted.Data.EntityValues);
                AppendSorted(sb, "l", posted.Data.LongValues);
                AppendSorted(sb, "d", posted.Data.DoubleValues);
                AppendSorted(sb, "b", posted.Data.BoolValues);
                AppendSorted(sb, "s", posted.Data.StringValues);
            }

            return sb.ToString();
        }

        private static void AppendSorted<T>(StringBuilder sb, string tag, IReadOnlyDictionary<string, T> values)
        {
            foreach (KeyValuePair<string, T> kvp in values.OrderBy(kvp => kvp.Key, System.StringComparer.Ordinal))
            {
                sb.Append(':').Append(tag).Append('.').Append(kvp.Key).Append('=').Append(kvp.Value);
            }
        }

        [Test]
        public void SameSeed_ThreeMonths_IdenticalStateHashAndEventLog()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (Simulation simA, List<string> logA) = Build(coreA, 42);
            (Simulation simB, List<string> logB) = Build(coreB, 42);

            for (long i = 0; i < CalendarConstants.TicksPerMonth * 3; i++)
            {
                simA.Step();
                simB.Step();
            }

            Assert.AreEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
            CollectionAssert.AreEqual(logA, logB);
            Assert.Greater(logA.Count, 0, "Sanity check: a 3-month run should post at least the calendar's own events, even with no modules.");
        }

        [Test]
        public void SaveAndLoad_TwiceAtDifferentPoints_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 123;
            long leg1 = CalendarConstants.TicksPerMonth; // first save point: one month in
            long leg2 = CalendarConstants.TicksPerMonth / 2; // second save point: mid next month
            long leg3 = CalendarConstants.TicksPerMonth * 3 / 2; // runs past a third month boundary

            (Simulation uninterrupted, _) = Build(core, seed);
            for (long i = 0; i < leg1 + leg2 + leg3; i++)
            {
                uninterrupted.Step();
            }

            ulong expectedHash = uninterrupted.ComputeStateHash();

            (Simulation firstRun, _) = Build(core, seed);
            for (long i = 0; i < leg1; i++)
            {
                firstRun.Step();
            }

            using var firstStream = new MemoryStream();
            GameSave.Save(firstRun, new SaveHeaderInfo { KingdomName = "K", Population = 8 }, firstStream);
            firstStream.Position = 0;

            (Simulation secondRun, _) = Load(firstStream, core);
            for (long i = 0; i < leg2; i++)
            {
                secondRun.Step();
            }

            using var secondStream = new MemoryStream();
            GameSave.Save(secondRun, new SaveHeaderInfo { KingdomName = "K", Population = 8 }, secondStream);
            secondStream.Position = 0;

            (Simulation thirdRun, _) = Load(secondStream, core);
            for (long i = 0; i < leg3; i++)
            {
                thirdRun.Step();
            }

            Assert.AreEqual(expectedHash, thirdRun.ComputeStateHash());
        }
    }
}
