using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;

namespace TheEmptying.Tests.EditMode.Content
{
    public class ContentDatabaseTests
    {
        private sealed class SimpleContent : ContentDefinition
        {
        }

        [Test]
        public void Set_ThenGet_ReturnsSameDefinition_WithSourceRecorded()
        {
            var database = new ContentDatabase();
            var definition = new SimpleContent { Id = "item.one" };

            database.Set("simple", definition, "core", "items.json");

            Assert.IsTrue(database.TryGet("simple", "item.one", out ContentEntry entry));
            Assert.AreSame(definition, entry.Definition);
            Assert.AreEqual("core", entry.SourceId);
            Assert.AreEqual("items.json", entry.SourceFile);
        }

        [Test]
        public void ListByType_ReturnsEntriesInAscendingIdOrder()
        {
            var database = new ContentDatabase();
            database.Set("simple", new SimpleContent { Id = "item.c" }, "core", "a.json");
            database.Set("simple", new SimpleContent { Id = "item.a" }, "core", "a.json");
            database.Set("simple", new SimpleContent { Id = "item.b" }, "core", "a.json");

            var ids = database.ListByType("simple").Select(e => e.Definition.Id).ToList();

            CollectionAssert.AreEqual(new[] { "item.a", "item.b", "item.c" }, ids);
        }

        [Test]
        public void Set_SameTypeAndId_OverridesEarlier_AndRecordsOverride()
        {
            var database = new ContentDatabase();
            var original = new SimpleContent { Id = "item.one" };
            var replacement = new SimpleContent { Id = "item.one" };

            database.Set("simple", original, "core", "a.json");
            database.Set("simple", replacement, "modA", "b.json");

            database.TryGet("simple", "item.one", out ContentEntry entry);
            Assert.AreSame(replacement, entry.Definition);
            Assert.AreEqual("modA", entry.SourceId);

            Assert.AreEqual(1, database.Overrides.Count);
            ContentOverride over = database.Overrides[0];
            Assert.AreEqual("simple", over.TypeName);
            Assert.AreEqual("item.one", over.Id);
            Assert.AreEqual("core", over.PreviousSourceId);
            Assert.AreEqual("modA", over.NewSourceId);
        }
    }
}
