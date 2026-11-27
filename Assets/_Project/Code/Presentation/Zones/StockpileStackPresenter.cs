using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R20/R22: draws every stockpile's stack spots as placeholder brown
    /// boxes - one box per third of a pile's capacity it holds (stage 1
    /// from any amount, stage 2 from a third, stage 3 from two thirds,
    /// dropping back as it empties) - plus each pile's own item icon,
    /// projected onto the UI as a small colored badge that shows a
    /// name/amount tooltip on hover. A spot can hold several piles
    /// (mixing); each is drawn in its own quarters, turning with the
    /// spot, sized to its shape and covering about
    /// PileBoxCoverageFractionSettingId of that footprint. The box models
    /// and icon sprites come from the item's own content asset once
    /// William's real art exists (ItemAsset.StackModels / Icon); until
    /// then every slot there is empty, so every box is this placeholder
    /// cube and every icon is the placeholder letter badge. Piles come
    /// and go as deliveries/takes happen, so (unlike the fixed spot list)
    /// pile visuals are reconciled every frame in Refresh - built the
    /// first time a pile is seen, torn down the moment its pile is gone.
    /// Icon badges are hidden together by the Overlays "Item icons"
    /// toggle; boxes are always shown (they're the stockpile itself, not
    /// an overlay).
    /// </summary>
    public sealed class StockpileStackPresenter : MonoBehaviour
    {
        /// <summary>A placeholder brown box's height, and the carried box's reference size - public so PersonPresenter's carried box can size itself relative to it.</summary>
        public const float BoxSizeMeters = 0.5f;

        /// <summary>R22: how much of a pile's own footprint (its quarters' area) the placeholder box covers, so spots don't look oversized for what's in them.</summary>
        public const string PileBoxCoverageFractionSettingId = "setting.zones.stockpile_pile_box_coverage_fraction";

        private const float BoxStackStepMeters = 0.42f;
        private const float BadgeSizePixels = 20f;
        private const float BadgeHeightAboveSpotMeters = 1.4f;

        private ContentDatabase _content;
        private SettingsRegistry _settings;
        private ZonesModule _zonesModule;
        private FloatingOrigin _floatingOrigin;
        private Camera _camera;
        private VisualElement _uiRoot;
        private Transform _root;

        private VisualElement _tooltip;
        private Label _tooltipLabel;

        private readonly Dictionary<StockPile, PileVisual> _pileVisuals = new Dictionary<StockPile, PileVisual>();
        private bool _iconsVisible = true;

        public void Initialize(ContentDatabase content, SettingsRegistry settings, FloatingOrigin floatingOrigin, Camera camera, VisualElement uiRoot)
        {
            _content = content;
            _settings = settings;
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

        /// <summary>After a load, the content database and settings registry are new instances.</summary>
        public void Rebind(ContentDatabase content, SettingsRegistry settings)
        {
            _content = content;
            _settings = settings;
        }

        public void SetIconsVisible(bool visible)
        {
            _iconsVisible = visible;
            if (!visible)
            {
                _tooltip.style.display = DisplayStyle.None;
            }

            foreach (PileVisual visual in _pileVisuals.Values)
            {
                visual.Badge.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Tears down and rebuilds every pile visual from scratch - call after a zone is created/removed, and after a load (which replaces the ZonesModule instance Update reads from every frame).</summary>
        public void Rebuild(ZonesModule zonesModule)
        {
            _zonesModule = zonesModule;

            foreach (PileVisual visual in _pileVisuals.Values)
            {
                DestroyPileVisual(visual);
            }

            _pileVisuals.Clear();

            Refresh();
        }

        /// <summary>Reconciles pile visuals (piles come and go every tick, unlike the fixed spot list) and refreshes each one's stage/badge, then repositions every badge - cheap, since nothing here rebuilds a GameObject unless a pile actually appeared or disappeared.</summary>
        private void Update()
        {
            Refresh();

            foreach (PileVisual visual in _pileVisuals.Values)
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

            var currentPiles = new HashSet<StockPile>();

            foreach (KeyValuePair<EntityId, ZoneState> row in _zonesModule.Stockpiles)
            {
                foreach (StockSpot spot in row.Value.Spots)
                {
                    foreach (StockPile pile in spot.Piles)
                    {
                        currentPiles.Add(pile);

                        if (!_pileVisuals.TryGetValue(pile, out PileVisual visual))
                        {
                            visual = BuildPileVisual(spot, pile);
                            _pileVisuals[pile] = visual;
                        }

                        ApplyPileState(visual, pile);
                    }
                }
            }

            List<StockPile> stale = _pileVisuals.Keys.Where(p => !currentPiles.Contains(p)).ToList();
            foreach (StockPile pile in stale)
            {
                DestroyPileVisual(_pileVisuals[pile]);
                _pileVisuals.Remove(pile);
            }
        }

        private PileVisual BuildPileVisual(StockSpot spot, StockPile pile)
        {
            double spotSize = _settings.Get(ZonesModule.SpotSizeMetersSettingId);
            double coverage = _settings.Get(PileBoxCoverageFractionSettingId);

            (double localX, double localZ, double width, double depth) = PileFootprint(pile.QuarterIndices, spotSize);
            (double worldX, double worldZ) = RotateAndOffset(localX, localZ, spot.RotationDegrees, spot.CenterX, spot.CenterZ);

            var boxesRoot = new GameObject("Pile");
            boxesRoot.transform.SetParent(_root, worldPositionStays: false);

            var boxes = new GameObject[3];
            for (int stage = 0; stage < 3; stage++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(boxesRoot.transform, worldPositionStays: false);
                Destroy(box.GetComponent<Collider>());
                box.transform.localScale = new Vector3((float)(width * coverage), BoxSizeMeters, (float)(depth * coverage));

                // R20a: the first box's bottom (not its pivot) rests on the ground; each stage above it stacks on the one below.
                float groundOffset = GroundPlacement.PivotHeightAboveBase(box);
                float jitterDegrees = PlaceholderAngleJitter(pile, stage);
                Vector3 worldPos = new Vector3((float)worldX, groundOffset + stage * BoxStackStepMeters, (float)worldZ);
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
                    display = _iconsVisible ? DisplayStyle.Flex : DisplayStyle.None,
                    width = BadgeSizePixels,
                    height = BadgeSizePixels,
                    borderTopLeftRadius = BadgeSizePixels * 0.5f,
                    borderTopRightRadius = BadgeSizePixels * 0.5f,
                    borderBottomLeftRadius = BadgeSizePixels * 0.5f,
                    borderBottomRightRadius = BadgeSizePixels * 0.5f,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    backgroundColor = PlaceholderBadgeColor(pile.ItemContentId)
                }
            };
            var badgeLabel = new Label
            {
                text = DisplayNameForItem(pile.ItemContentId).Substring(0, 1).ToUpperInvariant(),
                style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold }
            };
            badge.Add(badgeLabel);
            badge.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(badge));
            badge.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.style.display = DisplayStyle.None);
            _uiRoot.Add(badge);

            return new PileVisual
            {
                Pile = pile,
                BoxesRoot = boxesRoot,
                Boxes = boxes,
                Badge = badge,
                BadgeLabel = badgeLabel,
                WorldX = worldX,
                WorldZ = worldZ,
                LastStage = -1
            };
        }

        private void ApplyPileState(PileVisual visual, StockPile pile)
        {
            int capacity = CapacityOf(pile.ItemContentId);
            int stage = StageFor(pile.AmountStored, capacity);

            if (stage != visual.LastStage)
            {
                for (int i = 0; i < visual.Boxes.Length; i++)
                {
                    visual.Boxes[i].SetActive(i < stage);
                }

                visual.LastStage = stage;
            }

            visual.LastAmount = pile.AmountStored;
        }

        private void UpdateBadgePosition(PileVisual visual)
        {
            if (visual.Badge.style.display == DisplayStyle.None || _uiRoot.panel == null)
            {
                return;
            }

            Vector3 worldPos = new Vector3((float)visual.WorldX, BadgeHeightAboveSpotMeters, (float)visual.WorldZ);
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
            PileVisual visual = null;
            foreach (PileVisual candidate in _pileVisuals.Values)
            {
                if (candidate.Badge == badge)
                {
                    visual = candidate;
                    break;
                }
            }

            if (visual == null)
            {
                return;
            }

            _tooltipLabel.text = $"{DisplayNameForItem(visual.Pile.ItemContentId)}: {visual.LastAmount}";
            _tooltip.style.left = badge.style.left.value.value + BadgeSizePixels + 4f;
            _tooltip.style.top = badge.style.top.value.value;
            _tooltip.style.display = DisplayStyle.Flex;
        }

        private void DestroyPileVisual(PileVisual visual)
        {
            Destroy(visual.BoxesRoot);
            visual.Badge.RemoveFromHierarchy();
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

        /// <summary>The local (unrotated) center offset from the spot's own center, and the local width/depth, of a pile covering these quarters - see StockSpotQuarters for the quarter layout.</summary>
        private static (double LocalX, double LocalZ, double Width, double Depth) PileFootprint(IReadOnlyList<int> quarterIndices, double spotSizeMeters)
        {
            double quarterSize = spotSizeMeters * 0.5;
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (int quarter in quarterIndices)
            {
                int col = quarter % 2;
                int row = quarter / 2;
                double centerX = (col - 0.5) * quarterSize;
                double centerZ = (row - 0.5) * quarterSize;

                minX = Math.Min(minX, centerX - quarterSize * 0.5);
                maxX = Math.Max(maxX, centerX + quarterSize * 0.5);
                minZ = Math.Min(minZ, centerZ - quarterSize * 0.5);
                maxZ = Math.Max(maxZ, centerZ + quarterSize * 0.5);
            }

            return ((minX + maxX) * 0.5, (minZ + maxZ) * 0.5, maxX - minX, maxZ - minZ);
        }

        private static (double WorldX, double WorldZ) RotateAndOffset(double localX, double localZ, double rotationDegrees, double centerX, double centerZ)
        {
            double rad = rotationDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            return (centerX + localX * cos - localZ * sin, centerZ + localX * sin + localZ * cos);
        }

        private int CapacityOf(string itemContentId)
        {
            if (itemContentId == null || _zonesModule == null || !_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 1;
            }

            return _zonesModule.CapacityPerPile(item.PileShape, item.Size);
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
        private static float PlaceholderAngleJitter(StockPile pile, int stage)
        {
            int hash = pile.GetHashCode() ^ (stage * 7919);
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

        private sealed class PileVisual
        {
            public StockPile Pile;
            public GameObject BoxesRoot;
            public GameObject[] Boxes;
            public VisualElement Badge;
            public Label BadgeLabel;
            public int LastAmount;
            public int LastStage;
            public double WorldX;
            public double WorldZ;
        }
    }
}
