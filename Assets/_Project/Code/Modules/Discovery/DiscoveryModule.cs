using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;

namespace TheEmptying.Modules.Discovery
{
    /// <summary>
    /// R14: what's been discovered this game, by category - Items,
    /// Resources, Buildings, Actions, with more categories (crimes, say)
    /// meant to be added later without this module changing: a category
    /// is just a string key, and this module never hard-codes which ones
    /// exist. Never references another module directly - every trigger
    /// arrives as an event posted by whichever module caused it.
    /// </summary>
    public sealed class DiscoveryModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.discovery";

        public const string ItemCategory = "item";
        public const string ResourceCategory = "resource";
        public const string BuildingCategory = "building";
        public const string ActionCategory = "action";
        public const string ZoneCategory = "zone";

        /// <summary>Posted by People: a person dug, carried or ate an item.</summary>
        private const string ItemHeldEventId = "event.people.item_held";

        /// <summary>Posted by Zones: an item was put in a stockpile.</summary>
        private const string ItemStoredEventId = "event.zones.item_stored";

        /// <summary>Posted by People: a person finished harvesting from a resource.</summary>
        private const string ResourceHarvestedEventId = "event.people.resource_harvested";

        /// <summary>Posted by People: a person started a new action.</summary>
        private const string ActionPerformedEventId = "event.people.action_performed";

        /// <summary>Posted by Buildings: a building was placed.</summary>
        private const string BuildingPlacedEventId = "event.buildings.placed";

        /// <summary>Posted by Zones: a zone was placed.</summary>
        private const string ZonePlacedEventId = "event.zones.placed";

        /// <summary>The EventData field name every trigger event above carries its content Id under.</summary>
        private const string ContentIdField = "contentId";

        private ModuleContext _context;

        /// <summary>category -> content Id -> the tick it was first discovered.</summary>
        private readonly Dictionary<string, Dictionary<string, long>> _discovered = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

        /// <summary>Fires once, right after a genuinely new entry is recorded by a real trigger (never by DiscoverEverything) - category, content Id, the tick it happened. Presentation hooks this to show "Discovered: X" and to refresh its window.</summary>
        public event Action<string, string, long> Discovered;

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

        public void Initialize(ModuleContext context)
        {
            _context = context;

            context.Subscribe(ItemHeldEventId, posted => HandleTrigger(ItemCategory, posted));
            context.Subscribe(ItemStoredEventId, posted => HandleTrigger(ItemCategory, posted));
            context.Subscribe(ResourceHarvestedEventId, posted => HandleTrigger(ResourceCategory, posted));
            context.Subscribe(ActionPerformedEventId, posted => HandleTrigger(ActionCategory, posted));
            context.Subscribe(BuildingPlacedEventId, posted => HandleTrigger(BuildingCategory, posted));
            context.Subscribe(ZonePlacedEventId, posted => HandleTrigger(ZoneCategory, posted));
        }

        public void Shutdown()
        {
        }

        private void HandleTrigger(string category, PostedEvent posted)
        {
            if (posted.Data == null || !posted.Data.TryGetString(ContentIdField, out string contentId))
            {
                return;
            }

            if (TryRecord(category, contentId, posted.Tick))
            {
                Discovered?.Invoke(category, contentId, posted.Tick);
            }
        }

        public bool IsDiscovered(string category, string contentId)
        {
            return _discovered.TryGetValue(category, out Dictionary<string, long> byId) && byId.ContainsKey(contentId);
        }

        /// <summary>Every discovered entry in a category, content Id with the tick it was discovered, in discovery order (then by Id, for ties). Empty if the category has nothing discovered yet (or isn't known at all).</summary>
        public IReadOnlyList<(string ContentId, long Tick)> DiscoveredIn(string category)
        {
            if (!_discovered.TryGetValue(category, out Dictionary<string, long> byId))
            {
                return Array.Empty<(string, long)>();
            }

            return byId
                .Select(kvp => (kvp.Key, kvp.Value))
                .OrderBy(e => e.Item2)
                .ThenBy(e => e.Item1, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Test cheat: marks every known item, resource, building and action kind as discovered right now - silently (no "Discovered:" messages, no re-notifying what's already discovered).</summary>
        public void DiscoverEverything()
        {
            long tick = _context.CurrentTick;

            foreach (ContentEntry entry in _context.Content.ListByType(BuiltInContentTypes.ItemTypeName))
            {
                TryRecord(ItemCategory, entry.Definition.Id, tick);
            }

            foreach (ContentEntry entry in _context.Content.ListByType(BuiltInContentTypes.ResourceTypeName))
            {
                TryRecord(ResourceCategory, entry.Definition.Id, tick);
            }

            foreach (ContentEntry entry in _context.Content.ListByType(BuiltInContentTypes.BuildingTypeName))
            {
                TryRecord(BuildingCategory, entry.Definition.Id, tick);
            }

            foreach (ContentEntry entry in _context.Content.ListByType(BuiltInContentTypes.ActionDefinitionTypeName))
            {
                TryRecord(ActionCategory, entry.Definition.Id, tick);
            }

            foreach (ContentEntry entry in _context.Content.ListByType(BuiltInContentTypes.ZoneTypeTypeName))
            {
                TryRecord(ZoneCategory, entry.Definition.Id, tick);
            }
        }

        /// <summary>Test cheat: forgets every discovery. Undiscovered entries go back to being hidden.</summary>
        public void ResetDiscoveries()
        {
            _discovered.Clear();
        }

        /// <summary>Records contentId as discovered in category at tick, unless it already was. Returns whether this call actually recorded something new.</summary>
        private bool TryRecord(string category, string contentId, long tick)
        {
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(contentId))
            {
                return false;
            }

            if (!_discovered.TryGetValue(category, out Dictionary<string, long> byId))
            {
                byId = new Dictionary<string, long>(StringComparer.Ordinal);
                _discovered[category] = byId;
            }

            if (byId.ContainsKey(contentId))
            {
                return false;
            }

            byId[contentId] = tick;
            return true;
        }

        public void Write(SaveWriter writer)
        {
            List<string> categories = _discovered.Keys.OrderBy(c => c, StringComparer.Ordinal).ToList();
            writer.WriteInt(categories.Count);

            foreach (string category in categories)
            {
                Dictionary<string, long> byId = _discovered[category];
                writer.WriteString(category);
                writer.WriteInt(byId.Count);

                foreach (string contentId in byId.Keys.OrderBy(c => c, StringComparer.Ordinal))
                {
                    writer.WriteString(contentId);
                    writer.WriteLong(byId[contentId]);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int categoryCount = reader.ReadInt();
            for (int i = 0; i < categoryCount; i++)
            {
                string category = reader.ReadString();
                int idCount = reader.ReadInt();

                var byId = new Dictionary<string, long>(StringComparer.Ordinal);
                for (int j = 0; j < idCount; j++)
                {
                    string contentId = reader.ReadString();
                    long tick = reader.ReadLong();
                    byId[contentId] = tick;
                }

                _discovered[category] = byId;
            }
        }
    }
}
