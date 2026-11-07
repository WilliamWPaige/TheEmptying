using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Entities;

namespace TheEmptying.Tests.EditMode.Entities
{
    public class EntityTableTests
    {
        [Test]
        public void Add_ForDeadOrStaleId_Throws()
        {
            var registry = new EntityRegistry();
            var table = new EntityTable<int>(registry);

            EntityId id = registry.Create(EntityKind.Person);
            registry.Destroy(id);

            Assert.Throws<InvalidOperationException>(() => table.Add(id, 1));
        }

        [Test]
        public void GetByReference_EditsStoredValueInPlace()
        {
            var registry = new EntityRegistry();
            var table = new EntityTable<int>(registry);
            EntityId id = registry.Create(EntityKind.Person);
            table.Add(id, 10);

            ref int value = ref table.Get(id);
            value += 5;

            Assert.AreEqual(15, table.Get(id));
        }

        [Test]
        public void Rows_AreInAscendingIndexOrder_AfterMixedAddsAndRemoves_AndMatchFreshTable()
        {
            var registry = new EntityRegistry();
            var table = new EntityTable<string>(registry);

            var ids = new List<EntityId>();
            for (int i = 0; i < 20; i++)
            {
                EntityId id = registry.Create(EntityKind.Item);
                ids.Add(id);
                table.Add(id, $"value-{i}");
            }

            table.Remove(ids[7]);
            table.Remove(ids[2]);
            table.Remove(ids[15]);
            table.Remove(ids[0]);

            var rows = table.Rows().ToList();
            var indices = rows.Select(r => r.Key.Index).ToList();
            var sortedIndices = new List<int>(indices);
            sortedIndices.Sort();
            CollectionAssert.AreEqual(sortedIndices, indices);

            // A fresh table built directly with the same final contents, in index
            // order, should iterate out to the exact same values.
            var removedIndices = new HashSet<int> { ids[7].Index, ids[2].Index, ids[15].Index, ids[0].Index };
            var survivingIndices = Enumerable.Range(0, 20).Where(i => !removedIndices.Contains(i));

            var freshRegistry = new EntityRegistry();
            var freshTable = new EntityTable<string>(freshRegistry);
            foreach (int i in survivingIndices)
            {
                EntityId freshId = freshRegistry.Create(EntityKind.Item);
                freshTable.Add(freshId, $"value-{i}");
            }

            var freshValues = freshTable.Rows().Select(r => r.Value).ToList();
            var tableValues = rows.Select(r => r.Value).ToList();
            CollectionAssert.AreEqual(freshValues, tableValues);
        }
    }
}
