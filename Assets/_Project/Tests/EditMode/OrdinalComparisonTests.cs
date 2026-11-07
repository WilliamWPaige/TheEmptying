using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TheEmptying.Core.Content;

namespace TheEmptying.Tests.EditMode
{
    public class OrdinalComparisonTests
    {
        private sealed class SimpleContent : ContentDefinition
        {
        }

        private static T RunUnderCulture<T>(CultureInfo culture, Func<T> action)
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = culture;
                return action();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void SortingIds_UnderTurkishCulture_MatchesInvariantCultureOrder()
        {
            var ids = new List<string> { "item.Iron", "item.iron", "item.ID_5", "item.id_5", "Item.alpha", "item.Alpha" };

            List<string> Sort()
            {
                var copy = new List<string>(ids);
                copy.Sort(StringComparer.Ordinal);
                return copy;
            }

            List<string> invariantOrder = RunUnderCulture(CultureInfo.InvariantCulture, Sort);
            List<string> turkishOrder = RunUnderCulture(new CultureInfo("tr-TR"), Sort);

            CollectionAssert.AreEqual(invariantOrder, turkishOrder);
        }

        [Test]
        public void ContentDatabase_ListByType_UnderTurkishCulture_MatchesInvariantOrder()
        {
            List<string> BuildAndList()
            {
                var database = new ContentDatabase();
                database.Set("simple", new SimpleContent { Id = "item.Iron" }, "core", "a.json");
                database.Set("simple", new SimpleContent { Id = "item.iron" }, "core", "b.json");
                database.Set("simple", new SimpleContent { Id = "item.ID" }, "core", "c.json");
                database.Set("simple", new SimpleContent { Id = "item.id" }, "core", "d.json");

                return database.ListByType("simple").Select(e => e.Definition.Id).ToList();
            }

            List<string> invariant = RunUnderCulture(CultureInfo.InvariantCulture, BuildAndList);
            List<string> turkish = RunUnderCulture(new CultureInfo("tr-TR"), BuildAndList);

            CollectionAssert.AreEqual(invariant, turkish);
        }
    }
}
