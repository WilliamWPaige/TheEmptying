using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R20: draws every stockpile's stack spots as placeholder brown
    /// boxes (one box per third of the item's capacity the spot holds -
    /// stage 1 from any amount, stage 2 from a third, stage 3 from two
    /// thirds, dropping back as it empties), plus each non-empty spot's
    /// item icon, projected onto the UI as a small colored badge that
    /// shows a name/amount tooltip on hover. The box models and icon
    /// sprites come from the item's own content asset once William's
    /// real art exists (ItemAsset.StackModels / Icon); until then every
    /// slot there is empty, so every box is this placeholder cube and
    /// every icon is the placeholder letter badge. Not a per-frame
    /// rebuild: spot positions never change, so GameObjects are built
    /// once per Rebuild (after a zone is created/removed, or a load) and
    /// only their active box count and icon badge are refreshed as
    /// contents change. Icon badges are hidden together by the Overlays
    /// "Item icons" toggle; boxes are always shown (they're the
    /// stockpile itself, not an overlay).
    /// </summary>
    public sealed class StockpileStackPresenter : MonoBehaviour
    {
        private const float BoxSizeMeters = 0.5f;
        private const float BoxStackStepMeters = 0.42f;
        private const float BadgeSizePixels = 20f;
        private const float BadgeHeightAboveSpotMeters = 1.4f;

        private ContentDatabase _content;
        private ZonesModule _zonesModule;
        private FloatingOrigin _floatingOrigin;
        private Camera _camera;
        private VisualElement _uiRoot;
        private Transform _root;

        private VisualElement _tooltip;
        private Label _tooltipLabel;

        private readonly Dictionary<(EntityId ZoneId, int SpotIndex), SpotVisual> _spotVisuals = new Dictionary<(EntityId, int), SpotVisual>();
        private bool _iconsVisible = true;

        public void Initialize(ContentDatabase content, FloatingOrigin floatingOrigin, Camera camera, VisualElement uiRoot)
        {
            _content = content;
            _floatingOrigin = floatingOrigin;
            _camera = camera;
            _uiRoot = uiRoot;

            _root = new GameObject("StockpileStacks").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;

            _tooltipLabel = new Label { style = { color = Color.white } };
            _tooltip = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    backgroundColor = new Color(0f, 0f, 0f, 0.75f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 3,
                    paddingBottom = 3
                }
            };
            _tooltip.Add(_tooltipLabel);
            _uiRoot.Add(_tooltip);
        }

        /// <summary>After a load, the content database is a new instance.</summary>
        public void Rebind(ContentDatabase content)
        {
            _content = content;
        }

        public void SetIconsVisible(bool visible)
        {
            _iconsVisible = visible;
            if (!visible)
            {
                _tooltip.style.display = DisplayStyle.None;
            }

            foreach (SpotVisual visual in _spotVisuals.Values)
            {
                visual.Badge.style.display = visible && visual.LastItemId != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Rebuilds every stockpile spot's GameObjects and badge from scratch - call after a zone is created/removed, and after a load. Also replaces the ZonesModule instance Update reads from every frame (a fresh one after a load).</summary>
        public void Rebuild(ZonesModule zonesModule)
        {
            _zonesModule = zonesModule;

            foreach (SpotVisual visual in _spotVisuals.Values)
            {
                Destroy(visual.BoxesRoot);
                visual.Badge.RemoveFromHierarchy();
            }

            _spotVisuals.Clear();

            foreach (KeyValuePair<EntityId, ZoneState> row in zonesModule.Stockpiles)
            {
                ZoneState zone = row.Value;
                for (int i = 0; i < zone.Spots.Count; i++)
                {
                    _spotVisuals[(row.Key, i)] = BuildSpotVisual(zone.Spots[i]);
                }
            }

            Refresh();
        }

        /// <summary>Cheap per-frame sync: updates every spot's visible box count and badge content/position, without rebuilding any GameObject - amounts change far more often than zones are created or removed.</summary>
        private void Update()
        {
            Refresh();

            foreach (SpotVisual visual in _spotVisuals.Values)
            {
                UpdateBadgePosition(visual);
            }
        }

        private void Refresh()
        {
            if (_zonesModule == null)
            {
                return;
            }

            foreach (KeyValuePair<EntityId, ZoneState> row in _zonesModule.Stockpiles)
            {
                ZoneState zone = row.Value;
                for (int i = 0; i < zone.Spots.Count; i++)
                {
                    if (_spotVisuals.TryGetValue((row.Key, i), out SpotVisual visual))
                    {
                        ApplySpotState(visual, zone.Spots[i]);
                    }
                }
            }
        }

        private SpotVisual BuildSpotVisual(StockSpot spot)
        {
            var boxesRoot = new GameObject("Spot");
            boxesRoot.transform.SetParent(_root, worldPositionStays: false);

            var boxes = new GameObject[3];
            for (int stage = 0; stage < 3; stage++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(boxesRoot.transform, worldPositionStays: false);
                Object.Destroy(box.GetComponent<Collider>());
                box.transform.localScale = new Vector3(BoxSizeMeters, BoxSizeMeters, BoxSizeMeters);

                float jitterDegrees = PlaceholderAngleJitter(spot, stage);
                Vector3 worldPos = new Vector3((float)spot.CenterX, BoxSizeMeters * 0.5f + stage * BoxStackStepMeters, (float)spot.CenterZ);
                box.transform.position = _floatingOrigin.WorldToRender(worldPos);
                box.transform.rotation = Quaternion.Euler(0f, (float)spot.RotationDegrees + jitterDegrees, 0f);
                ApplyPlaceholderBrownMaterial(box, stage);
                box.SetActive(false);
                boxes[stage] = box;
            }

            var badge = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    width = BadgeSizePixels,
                    height = BadgeSizePixels,
                    borderTopLeftRadius = BadgeSizePixels * 0.5f,
                    borderTopRightRadius = BadgeSizePixels * 0.5f,
                    borderBottomLeftRadius = BadgeSizePixels * 0.5f,
                    borderBottomRightRadius = BadgeSizePixels * 0.5f,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center
                }
            };
            var badgeLabel = new Label { style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } };
            badge.Add(badgeLabel);
            badge.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(badge));
            badge.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.style.display = DisplayStyle.None);
            _uiRoot.Add(badge);

            return new SpotVisual
            {
                Spot = spot,
                BoxesRoot = boxesRoot,
                Boxes = boxes,
                Badge = badge,
                BadgeLabel = badgeLabel
            };
        }

        private void ApplySpotState(SpotVisual visual, StockSpot spot)
        {
            int capacityPerSpot = CapacityPerSpotOf(spot.ItemContentId);
            int stage = StageFor(spot.AmountStored, capacityPerSpot);

            if (stage != visual.LastStage)
            {
                for (int i = 0; i < visual.Boxes.Length; i++)
                {
                    visual.Boxes[i].SetActive(i < stage);
                }

                visual.LastStage = stage;
            }

            if (spot.ItemContentId != visual.LastItemId)
            {
                visual.LastItemId = spot.ItemContentId;

                if (spot.ItemContentId != null)
                {
                    visual.BadgeLabel.text = DisplayNameForItem(spot.ItemContentId).Substring(0, 1).ToUpperInvariant();
                    visual.Badge.style.backgroundColor = PlaceholderBadgeColor(spot.ItemContentId);
                }

                visual.Badge.style.display = spot.ItemContentId != null && _iconsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            visual.LastAmount = spot.AmountStored;
        }

        private void UpdateBadgePosition(SpotVisual visual)
        {
            if (visual.Badge.style.display == DisplayStyle.None || _uiRoot.panel == null)
            {
                return;
            }

            Vector3 worldPos = new Vector3((float)visual.Spot.CenterX, BadgeHeightAboveSpotMeters, (float)visual.Spot.CenterZ);
            Vector3 renderPos = _floatingOrigin.WorldToRender(worldPos);
            Vector3 viewportPoint = _camera.WorldToViewportPoint(renderPos);

            if (viewportPoint.z <= 0f || viewportPoint.x < 0f || viewportPoint.x > 1f || viewportPoint.y < 0f || viewportPoint.y > 1f)
            {
                visual.Badge.style.visibility = Visibility.Hidden;
                return;
            }

            visual.Badge.style.visibility = Visibility.Visible;

            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_uiRoot.panel, renderPos, _camera);
            visual.Badge.style.left = panelPosition.x - BadgeSizePixels * 0.5f;
            visual.Badge.style.top = panelPosition.y - BadgeSizePixels * 0.5f;
        }

        private void ShowTooltip(VisualElement badge)
        {
            SpotVisual visual = null;
            foreach (SpotVisual candidate in _spotVisuals.Values)
            {
                if (candidate.Badge == badge)
                {
                    visual = candidate;
                    break;
                }
            }

            if (visual?.LastItemId == null)
            {
                return;
            }

            _tooltipLabel.text = $"{DisplayNameForItem(visual.LastItemId)}: {visual.LastAmount}";
            _tooltip.style.left = badge.style.left.value.value + BadgeSizePixels + 4f;
            _tooltip.style.top = badge.style.top.value.value;
            _tooltip.style.display = DisplayStyle.Flex;
        }

        /// <summary>Stage 1 from any amount, stage 2 from a third of capacity, stage 3 from two thirds (placeholders) - 0 while empty.</summary>
        public static int StageFor(int amountStored, int capacityPerSpot)
        {
            if (amountStored <= 0 || capacityPerSpot <= 0)
            {
                return 0;
            }

            double fraction = (double)amountStored / capacityPerSpot;
            if (fraction >= 2.0 / 3.0)
            {
                return 3;
            }

            return fraction >= 1.0 / 3.0 ? 2 : 1;
        }

        private int CapacityPerSpotOf(string itemContentId)
        {
            if (itemContentId == null || _zonesModule == null || !_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 1;
            }

            return _zonesModule.CapacityPerSpot(item.Size);
        }

        private string DisplayNameForItem(string itemContentId)
        {
            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.DisplayName;
            }

            return itemContentId;
        }

        /// <summary>A slight random horizontal angle per placeholder box (design, Part 3) - a plain visual flourish, not simulation state, so a cheap deterministic-looking hash is enough; it need not survive save/load matching exactly.</summary>
        private static float PlaceholderAngleJitter(StockSpot spot, int stage)
        {
            int hash = spot.CenterX.GetHashCode() ^ spot.CenterZ.GetHashCode() ^ (stage * 7919);
            return (hash % 2000) / 100f - 10f; // -10..+10 degrees
        }

        private static void ApplyPlaceholderBrownMaterial(GameObject box, int stage)
        {
            var renderer = box.GetComponent<MeshRenderer>();
            float shade = 0.35f + stage * 0.08f;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            Color brown = new Color(shade, shade * 0.65f, shade * 0.35f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", brown);
            }
            else
            {
                material.color = brown;
            }

            renderer.sharedMaterial = material;
        }

        /// <summary>A stand-in for the item's own color until real icons exist - stable per item id, so the same item always gets the same badge color.</summary>
        private static Color PlaceholderBadgeColor(string itemContentId)
        {
            int hash = itemContentId.GetHashCode();
            float hue = (hash & 0xFFFF) / (float)0xFFFF;
            return Color.HSVToRGB(hue, 0.55f, 0.85f);
        }

        private sealed class SpotVisual
        {
            public StockSpot Spot;
            public GameObject BoxesRoot;
            public GameObject[] Boxes;
            public VisualElement Badge;
            public Label BadgeLabel;
            public string LastItemId;
            public int LastAmount;
            public int LastStage;
        }
    }
}
