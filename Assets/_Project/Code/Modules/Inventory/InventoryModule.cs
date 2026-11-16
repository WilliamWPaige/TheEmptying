using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;

namespace TheEmptying.Modules.Inventory
{
    /// <summary>
    /// Goods owned by an entity: a person now, businesses and the crown
    /// storehouse later. Just storage - the market module builds the economy
    /// on top of this in 3.9.
    /// </summary>
    public sealed class InventoryModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.inventory";

        private EntityTable<InventoryState> _inventories;

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _inventories = new EntityTable<InventoryState>(context.Entities);

            context.RegisterCommand(new ConsoleCommand("inventory", "inventory <entityId> - lists an entity's goods.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id))
                {
                    return "Usage: inventory <entityId> (e.g. e12.3)";
                }

                List<string> lines = GetAll(id).Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();
                return lines.Count == 0 ? $"{id} has no goods." : string.Join("\n", lines);
            }));
        }

        public void Shutdown()
        {
        }

        #region Public API

        public bool HasInventory(EntityId entity) => _inventories.Has(entity);

        /// <summary>Adds amount of goodId to entity's inventory, creating the inventory if this is its first good. amount must be positive.</summary>
        public void Add(EntityId entity, string goodId, int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive.");
            }

            if (!_inventories.TryGet(entity, out InventoryState state))
            {
                state = new InventoryState();
                _inventories.Add(entity, state);
            }

            state.Goods.TryGetValue(goodId, out int current);
            state.Goods[goodId] = current + amount;
        }

        /// <summary>Removes up to amount of goodId. False (no change) if entity has fewer than amount - a count never goes below zero.</summary>
        public bool TryRemove(EntityId entity, string goodId, int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive.");
            }

            if (!_inventories.TryGet(entity, out InventoryState state) || !state.Goods.TryGetValue(goodId, out int current) || current < amount)
            {
                return false;
            }

            int remaining = current - amount;
            if (remaining == 0)
            {
                state.Goods.Remove(goodId);
            }
            else
            {
                state.Goods[goodId] = remaining;
            }

            return true;
        }

        public int GetCount(EntityId entity, string goodId)
        {
            if (_inventories.TryGet(entity, out InventoryState state) && state.Goods.TryGetValue(goodId, out int count))
            {
                return count;
            }

            return 0;
        }

        /// <summary>Every good the entity owns, in ascending Id order. Empty if it has no inventory at all.</summary>
        public IEnumerable<KeyValuePair<string, int>> GetAll(EntityId entity)
        {
            if (!_inventories.TryGet(entity, out InventoryState state))
            {
                return Enumerable.Empty<KeyValuePair<string, int>>();
            }

            return state.Goods.OrderBy(kvp => kvp.Key, StringComparer.Ordinal);
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_inventories.Count);
            foreach (KeyValuePair<EntityId, InventoryState> row in _inventories.Rows())
            {
                writer.WriteEntityId(row.Key);
                List<KeyValuePair<string, int>> goods = row.Value.Goods.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToList();
                writer.WriteInt(goods.Count);
                foreach (KeyValuePair<string, int> good in goods)
                {
                    writer.WriteString(good.Key);
                    writer.WriteInt(good.Value);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var state = new InventoryState();

                int goodCount = reader.ReadInt();
                for (int g = 0; g < goodCount; g++)
                {
                    string goodId = reader.ReadString();
                    int amount = reader.ReadInt();
                    state.Goods[goodId] = amount;
                }

                _inventories.Add(id, state);
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            foreach (KeyValuePair<string, int> good in GetAll(entity))
            {
                yield return $"{good.Key}: {good.Value}";
            }
        }

        #endregion
    }
}
