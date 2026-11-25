using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Discovery;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Zones;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Discovery
{
    /// <summary>
    /// R14: the Discoveries window - Resources/Items/Buildings/Actions
    /// tabs, each listing only what's been discovered; clicking an entry
    /// shows its details. Built entirely in C# (like InfoWindowPresenter),
    /// positioned to clear both the debug window (left, up to x=496) and
    /// the info window (right, width 300) on a normal-width screen.
    /// </summary>
    public sealed class DiscoveriesWindowPresenter : MonoBehaviour
    {
        private DiscoveryModule _discoveryModule;
        private ContentDatabase _content;

        private VisualElement _root;
        private VisualElement _tabRow;
        private readonly System.Collections.Generic.Dictionary<string, Button> _tabButtons = new System.Collections.Generic.Dictionary<string, Button>();
        private ScrollView _listView;
        private ScrollView _detailView;

        private string _activeCategory = DiscoveryModule.ResourceCategory;
        private string _selectedContentId;

        /// <summary>R14: what was open right before the Escape menu force-closed this window, for ForceReopen to restore; false if it wasn't open at the time.</summary>
        private bool _wasOpenBeforeModalClose;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(VisualElement uiRoot, DiscoveryModule discoveryModule, ContentDatabase content)
        {
            _discoveryModule = discoveryModule;
            _content = content;
            BuildUi(uiRoot);
        }

        /// <summary>After a load, the simulation (and its modules) are new instances; this window closes and starts fresh next time it's opened.</summary>
        public void Rebind(DiscoveryModule discoveryModule, ContentDatabase content)
        {
            _discoveryModule = discoveryModule;
            _content = content;
            _selectedContentId = null;
            Close();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            _root.style.display = DisplayStyle.Flex;
            RefreshList();
        }

        public void Close()
        {
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        public void HandleEscapePressed() => Close();

        /// <summary>Called whenever a new discovery is recorded, so the list picks it up immediately if this window happens to already be open.</summary>
        public void RefreshIfOpen()
        {
            if (IsOpen)
            {
                RefreshList();
            }
        }

        /// <summary>R9-style modal rule: the Escape menu force-closes this window while open, remembering whether it was open so Close() can restore it.</summary>
        public void ForceCloseForModal()
        {
            _wasOpenBeforeModalClose = IsOpen;
            if (_wasOpenBeforeModalClose)
            {
                Close();
            }
        }

        public void ForceReopenAfterModal()
        {
            if (!_wasOpenBeforeModalClose)
            {
                return;
            }

            _wasOpenBeforeModalClose = false;
            Open();
        }

        private void BuildUi(VisualElement uiRoot)
        {
            _root = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 512,
                    top = 64,
                    bottom = 40,
                    width = 460,
                    backgroundColor = new Color(0f, 0f, 0f, 0.65f),
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 8,
                    paddingBottom = 12,
                    flexDirection = FlexDirection.Column,
                    display = DisplayStyle.None
                }
            };

            var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 6 } };
            headerRow.Add(new Label("Discoveries") { style = { color = Color.white, fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold } });
            headerRow.Add(new Button(Close) { text = "X", style = { width = 26, height = 26 } });
            _root.Add(headerRow);

            _tabRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            AddTabButton(DiscoveryModule.ResourceCategory, "Resources");
            AddTabButton(DiscoveryModule.ItemCategory, "Items");
            AddTabButton(DiscoveryModule.BuildingCategory, "Buildings");
            AddTabButton(DiscoveryModule.ActionCategory, "Actions");
            AddTabButton(DiscoveryModule.ZoneCategory, "Zones");
            _root.Add(_tabRow);

            var bodyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

            _listView = new ScrollView { style = { width = Length.Percent(42), marginRight = 8 } };
            bodyRow.Add(_listView);

            _detailView = new ScrollView { style = { flexGrow = 1 } };
            bodyRow.Add(_detailView);

            _root.Add(bodyRow);

            uiRoot.Add(_root);
            UpdateTabButtonStyles();
        }

        private void AddTabButton(string category, string label)
        {
            var button = new Button(() => SwitchTab(category)) { text = label, style = { marginRight = 4, flexGrow = 1 } };
            _tabButtons[category] = button;
            _tabRow.Add(button);
        }

        private void SwitchTab(string category)
        {
            _activeCategory = category;
            _selectedContentId = null;
            UpdateTabButtonStyles();
            RefreshList();
        }

        private void UpdateTabButtonStyles()
        {
            foreach (System.Collections.Generic.KeyValuePair<string, Button> entry in _tabButtons)
            {
                bool active = entry.Key == _activeCategory;
                entry.Value.style.backgroundColor = active ? new Color(0.3f, 0.5f, 0.8f) : new StyleColor(StyleKeyword.Null);
                entry.Value.style.color = active ? Color.white : new StyleColor(StyleKeyword.Null);
            }
        }

        private void RefreshList()
        {
            _listView.Clear();

            foreach ((string contentId, long tick) in _discoveryModule.DiscoveredIn(_activeCategory))
            {
                string displayName = ResolveDisplayName(_activeCategory, contentId);
                string id = contentId;

                var row = new Button(() => SelectEntry(id)) { text = displayName, style = { marginBottom = 2, unityTextAlign = TextAnchor.MiddleLeft } };
                _listView.Add(row);
            }

            if (_selectedContentId != null && !_discoveryModule.IsDiscovered(_activeCategory, _selectedContentId))
            {
                _selectedContentId = null;
            }

            RefreshDetail();
        }

        private void SelectEntry(string contentId)
        {
            _selectedContentId = contentId;
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            _detailView.Clear();

            if (_selectedContentId == null)
            {
                _detailView.Add(new Label("Select a discovered entry to see its details.") { style = { color = new Color(0.7f, 0.7f, 0.7f), whiteSpace = WhiteSpace.Normal } });
                return;
            }

            long tick = _discoveryModule.DiscoveredIn(_activeCategory).FirstOrDefault(e => e.ContentId == _selectedContentId).Tick;

            if (_activeCategory == DiscoveryModule.ItemCategory)
            {
                BuildItemDetail(_selectedContentId, tick);
            }
            else if (_activeCategory == DiscoveryModule.ResourceCategory)
            {
                BuildResourceDetail(_selectedContentId, tick);
            }
            else if (_activeCategory == DiscoveryModule.BuildingCategory)
            {
                BuildBuildingDetail(_selectedContentId, tick);
            }
            else if (_activeCategory == DiscoveryModule.ActionCategory)
            {
                BuildActionDetail(_selectedContentId, tick);
            }
            else if (_activeCategory == DiscoveryModule.ZoneCategory)
            {
                BuildZoneDetail(_selectedContentId, tick);
            }
        }

        private void BuildItemDetail(string itemId, long tick)
        {
            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return;
            }

            AddDetailTitle(item.DisplayName);

            string comesFrom = string.Join(", ", _content.ListByType(BuiltInContentTypes.ResourceTypeName)
                .Select(e => (ResourceContent)e.Definition)
                .Where(r => !r.YieldedItem.IsEmpty && r.YieldedItem.Id == itemId)
                .Select(r => r.DisplayName));

            AddDetailRow("Comes from", string.IsNullOrEmpty(comesFrom) ? "(nothing known)" : comesFrom);
            AddDetailRow("Food value", item.FoodValue.ToString("0.#"));
            AddDetailRow("Carry amount", item.CarryAmount.ToString());
            AddDetailRow("Base value", PeopleModule.FormatTenthCopper(item.BaseValueTenthCopper));
            AddDetailRow("Size", item.Size.ToString());
            AddDetailRow("Spoil time", item.SpoilTimeMinutes <= 0 ? "Never" : $"{item.SpoilTimeMinutes:0.#} min");
            AddDiscoveredRow(tick);
        }

        private void BuildResourceDetail(string resourceId, long tick)
        {
            if (!_content.TryGet(BuiltInContentTypes.ResourceTypeName, resourceId, out ContentEntry entry) || !(entry.Definition is ResourceContent resource))
            {
                return;
            }

            AddDetailTitle(resource.DisplayName);

            string yields = resource.YieldedItem.IsEmpty ? "(nothing)" : DisplayNameForItem(resource.YieldedItem.Id);
            AddDetailRow("Yields", yields);
            AddDetailRow("Amount held", resource.UnlimitedAmount ? "Unlimited" : resource.AmountHeld.ToString());
            AddDetailRow("Unskilled harvest time", $"{resource.HarvestTimeUnskilledMinutes:0.#} min");
            AddDetailRow("Skilled harvest time", $"{resource.HarvestTimeSkilledMinutes:0.#} min");
            AddDetailRow("Master harvest time", $"{resource.HarvestTimeMasterMinutes:0.#} min");
            AddDetailRow("Max workers", resource.MaxWorkers.ToString());

            if (!resource.GatherItem.IsEmpty)
            {
                AddDetailRow("Gather nearby", DisplayNameForItem(resource.GatherItem.Id));
                AddDetailRow("Gather radius", $"{resource.GatherRadiusMeters:0.#} m");
                AddDetailRow("Gather max at once", resource.GatherMaxConcurrent.ToString());
                AddDetailRow("Gather time (Unskilled/Skilled/Master)", $"{resource.GatherTimeUnskilledMinutes:0.#} / {resource.GatherTimeSkilledMinutes:0.#} / {resource.GatherTimeMasterMinutes:0.#} min");

                if (!resource.GatherBonusItem.IsEmpty)
                {
                    AddDetailRow("Bonus chance", $"{resource.GatherBonusChance * 100:0.#}% {DisplayNameForItem(resource.GatherBonusItem.Id)}");
                }
            }

            AddDiscoveredRow(tick);
        }

        private void BuildBuildingDetail(string buildingId, long tick)
        {
            if (!_content.TryGet(BuiltInContentTypes.BuildingTypeName, buildingId, out ContentEntry entry) || !(entry.Definition is BuildingContent building))
            {
                return;
            }

            AddDetailTitle(building.DisplayName);
            AddDetailRow("Footprint", $"{building.FootprintWidth} x {building.FootprintDepth}");
            AddDiscoveredRow(tick);
        }

        private void BuildActionDetail(string actionId, long tick)
        {
            if (!_content.TryGet(BuiltInContentTypes.ActionDefinitionTypeName, actionId, out ContentEntry entry) || !(entry.Definition is ActionDefinitionContent action))
            {
                return;
            }

            AddDetailTitle(action.DisplayName);
            _detailView.Add(new Label(action.Description) { style = { color = new Color(0.85f, 0.85f, 0.85f), marginLeft = 8, marginBottom = 8, whiteSpace = WhiteSpace.Normal } });
            AddDiscoveredRow(tick);
        }

        private void BuildZoneDetail(string zoneTypeId, long tick)
        {
            if (!_content.TryGet(BuiltInContentTypes.ZoneTypeTypeName, zoneTypeId, out ContentEntry entry) || !(entry.Definition is ZoneTypeDefinition zoneType))
            {
                return;
            }

            AddDetailTitle(zoneType.DisplayName);

            if (ColorUtility.TryParseHtmlString(zoneType.ColorHex, out Color color))
            {
                var swatchRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8, marginBottom = 6 } };
                swatchRow.Add(new VisualElement { style = { width = 14, height = 14, marginRight = 6, backgroundColor = color } });
                swatchRow.Add(new Label(zoneType.ColorHex) { style = { color = Color.white } });
                _detailView.Add(swatchRow);
            }

            _detailView.Add(new Label(zoneType.Description)
            {
                style = { color = new Color(0.85f, 0.85f, 0.85f), marginLeft = 8, marginBottom = 8, whiteSpace = WhiteSpace.Normal }
            });

            if (zoneTypeId == ZonesModule.StockpileTypeId)
            {
                AddDetailRow("Size units per slot", ZonesModule.CapacityUnitsPerSlot.ToString());
            }

            AddDiscoveredRow(tick);
        }

        private string DisplayNameForItem(string itemId)
        {
            return _content.TryGet(BuiltInContentTypes.ItemTypeName, itemId, out ContentEntry entry) && entry.Definition is ItemContent item
                ? item.DisplayName
                : itemId;
        }

        private string ResolveDisplayName(string category, string contentId)
        {
            string typeName = CategoryToTypeName(category);
            if (typeName == null || !_content.TryGet(typeName, contentId, out ContentEntry entry))
            {
                return contentId;
            }

            if (entry.Definition is ItemContent item) return item.DisplayName;
            if (entry.Definition is ResourceContent resource) return resource.DisplayName;
            if (entry.Definition is BuildingContent building) return building.DisplayName;
            if (entry.Definition is ActionDefinitionContent action) return action.DisplayName;
            if (entry.Definition is ZoneTypeDefinition zoneType) return zoneType.DisplayName;
            return contentId;
        }

        private static string CategoryToTypeName(string category)
        {
            if (category == DiscoveryModule.ItemCategory) return BuiltInContentTypes.ItemTypeName;
            if (category == DiscoveryModule.ResourceCategory) return BuiltInContentTypes.ResourceTypeName;
            if (category == DiscoveryModule.BuildingCategory) return BuiltInContentTypes.BuildingTypeName;
            if (category == DiscoveryModule.ActionCategory) return BuiltInContentTypes.ActionDefinitionTypeName;
            if (category == DiscoveryModule.ZoneCategory) return BuiltInContentTypes.ZoneTypeTypeName;
            return null;
        }

        private void AddDetailTitle(string text)
        {
            _detailView.Add(new Label(text) { style = { color = Color.white, fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
        }

        private void AddDetailRow(string label, string value)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginLeft = 8, marginBottom = 2 } };
            row.Add(new Label(label) { style = { color = new Color(0.75f, 0.75f, 0.75f) } });
            row.Add(new Label(value) { style = { color = Color.white, whiteSpace = WhiteSpace.Normal, maxWidth = 220 } });
            _detailView.Add(row);
        }

        private void AddDiscoveredRow(long tick)
        {
            GameDate date = GameDate.FromTick(tick);
            AddDetailRow("Discovered", date.ToString());
        }
    }
}
