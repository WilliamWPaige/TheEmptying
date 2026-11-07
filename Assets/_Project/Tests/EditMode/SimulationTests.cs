using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Events;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode
{
    public class SimulationTests
    {
        [Test]
        public void OneFullDay_PostsExactlyTwentyFourNewHourEvents_AndOneNewDayEvent_OnCorrectTicks()
        {
            var sim = new Simulation(12345);
            var hourTicks = new List<long>();
            var dayTicks = new List<long>();

            sim.Events.Subscribe<NewHourEvent>(e => hourTicks.Add(e.Tick));
            sim.Events.Subscribe<NewDayEvent>(e => dayTicks.Add(e.Tick));

            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(24, hourTicks.Count);
            Assert.AreEqual(1, dayTicks.Count);

            var expectedHourTicks = new List<long>();
            for (int hour = 1; hour <= 24; hour++)
            {
                expectedHourTicks.Add(hour * CalendarConstants.TicksPerHour);
            }

            CollectionAssert.AreEqual(expectedHourTicks, hourTicks);
            Assert.AreEqual(CalendarConstants.TicksPerDay, dayTicks[0]);
        }

        [Test]
        public void TwoSimulations_SameSeed_SameSteps_ProduceIdenticalEventLogs()
        {
            const ulong seed = 999;
            const int steps = 2000;

            var simA = new Simulation(seed);
            var simB = new Simulation(seed);

            var logA = new List<string>();
            var logB = new List<string>();

            void Subscribe(Simulation sim, List<string> log)
            {
                sim.Events.Subscribe<NewHourEvent>(e => log.Add($"hour:{e.Tick}"));
                sim.Events.Subscribe<NewDayEvent>(e => log.Add($"day:{e.Tick}"));
                sim.Events.Subscribe<NewSeasonEvent>(e => log.Add($"season:{e.Tick}"));
                sim.Events.Subscribe<NewYearEvent>(e => log.Add($"year:{e.Tick}"));
            }

            Subscribe(simA, logA);
            Subscribe(simB, logB);

            for (int i = 0; i < steps; i++)
            {
                simA.Step();
                simB.Step();
            }

            Assert.IsNotEmpty(logA);
            CollectionAssert.AreEqual(logA, logB);
        }
    }
}
