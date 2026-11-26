using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.World;
using TheEmptying.Presentation.Zones;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.People
{
    /// <summary>
    /// R7/R8: draws each living person with its type's model, through the
    /// floating origin, only while their chunk is loaded. Selection and
    /// the info window live in WorldSelectionPresenter, which uses
    /// TryGetEntityForInstance to tell a clicked person apart from
    /// anything else. R10: the rendered position eases toward the
    /// simulation's own position every frame (Time.deltaTime, allowed
    /// here in Presentation) rather than snapping to it - ticks happen
    /// only a handful of times a second, so without this, walking would
    /// visibly step rather than glide, at any game speed. R11: never
    /// drawn while waiting inside a building. R20a: the model's own
    /// pivot may not sit at its base, so its ground offset is measured
    /// from its own bounds (GroundPlacement) rather than assumed to be
    /// zero, and a person carrying an item shows a small box in front
    /// of them at about chest height, from that item's stack models
    /// (stage 1, picked at random each time they start carrying
    /// something new), hidden again the moment they no longer carry
    /// anything.
    /// </summary>
    public sealed class PersonPresenter : MonoBehaviour
    {
        /// <summary>Not a setting - a pure rendering nicety (how quickly the visible position catches up to the simulation's own), not a tunable the design calls out.</summary>
        private const float PositionSmoothingRate = 12f;

        public const string CarriedBoxScaleSettingId = "setting.people.carried_box_scale";

        /// <summary>Not settings - plain rendering niceties (placeholder, until carried items get a real design), same footing as PositionSmoothingRate.</summary>
        private const float CarriedBoxHeightAboveGroundMeters = 1.2f;
        private const float CarriedBoxForwardMeters = 0.35f;

        private PeopleModule _peopleModule;
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;
        private WorldMapView _worldMapView;
        private SettingsRegistry _settings;

        private readonly Dictionary<string, UnitAsset> _unitAssetsById = new Dictionary<string, UnitAsset>();
        private readonly Dictionary<string, ItemAsset> _itemAssetsById = new Dictionary<string, ItemAsset>();
        private readonly Dictionary<UnitAsset, float> _groundOffsetByAsset = new Dictionary<UnitAsset, float>();
        private readonly Dictionary<EntityId, GameObject> _activeInstances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();
        private readonly Dictionary<EntityId, Vector3> _smoothedWorldPosition = new Dictionary<EntityId, Vector3>();
        private readonly Dictionary<EntityId, GameObject> _carriedBoxInstances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<EntityId, string> _carriedBoxItemId = new Dictionary<EntityId, string>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly List<EntityId> _toHide = new List<EntityId>();

        private Transform _root;

        public void Initialize(PeopleModule peopleModule, MapModule mapModule, FloatingOrigin floatingOrigin, WorldMapView worldMapView, SettingsRegistry settings)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;
            _worldMapView = worldMapView;
            _settings = settings;

            _root = new GameObject("People").transform;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            if (catalog != null)
            {
                foreach (UnitAsset asset in catalog.Units)
                {
                    if (asset != null)
                    {
                        _unitAssetsById[asset.Id] = asset;
                    }
                }

                foreach (ItemAsset asset in catalog.Items)
                {
                    if (asset != null)
                    {
                        _itemAssetsById[asset.Id] = asset;
                    }
                }
            }
        }

        /// <summary>After a load, the simulation (and its modules) are new instances; every instance goes back to the pool, since the old EntityIds no longer mean anything.</summary>
        public void Rebind(PeopleModule peopleModule, MapModule mapModule, SettingsRegistry settings)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _settings = settings;

            foreach (GameObject instance in _activeInstances.Values)
            {
                instance.SetActive(false);
                _pool.Push(instance);
            }

            foreach (GameObject box in _carriedBoxInstances.Values)
            {
                Object.Destroy(box);
            }

            _activeInstances.Clear();
            _entityByInstance.Clear();
            _smoothedWorldPosition.Clear();
            _carriedBoxInstances.Clear();
            _carriedBoxItemId.Clear();
        }

        /// <summary>Resolves a clicked GameObject back to the person it's currently drawing, if any.</summary>
        public bool TryGetEntityForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

        /// <summary>R18: hides or shows every currently-drawn person at once (the zoning view hides people).</summary>
        public void SetVisible(bool visible) => _root.gameObject.SetActive(visible);

        private void Update()
        {
            if (_peopleModule == null)
            {
                return;
            }

            UpdateInstances();
        }

        private void UpdateInstances()
        {
            var seen = new HashSet<EntityId>();

            foreach (KeyValuePair<EntityId, PersonState> row in _peopleModule.People)
            {
                EntityId id = row.Key;
                PersonState person = row.Value;
                seen.Add(id);

                if (person.CurrentAction != null && person.CurrentAction.Kind == ActionKind.InsideBuilding)
                {
                    // R11: waiting inside a building - not drawn, regardless of chunk state.
                    HideInstance(id);
                    continue;
                }

                ChunkCoord chunk = _mapModule.WorldToChunk(person.WorldX, person.WorldZ);
                if (!_worldMapView.IsChunkLoaded(chunk))
                {
                    HideInstance(id);
                    continue;
                }

                if (!_unitAssetsById.TryGetValue(person.UnitContentId, out UnitAsset asset) || asset.Model == null)
                {
                    continue;
                }

                var worldPosition = new Vector3((float)person.WorldX, 0f, (float)person.WorldZ);
                bool justShown = false;

                if (!_activeInstances.TryGetValue(id, out GameObject instance))
                {
                    instance = _pool.Count > 0 ? _pool.Pop() : Object.Instantiate(asset.Model, _root);
                    instance.SetActive(true);
                    _activeInstances[id] = instance;
                    _entityByInstance[instance] = id;
                    justShown = true;
                }

                // R20a: the model's own pivot may not be at its base - lift the whole person so the model's bottom, not its pivot, rests on the ground.
                float groundOffset = GroundOffsetFor(asset, instance);
                worldPosition.y = groundOffset;

                // R10: ease toward the simulation's position in world space (never shifted), so a floating-origin shift mid-smooth can't cause a jump; snap instantly the first frame an instance is shown, so it never visibly slides in from wherever it (or the pooled object before it) last stood.
                Vector3 smoothed = justShown || !_smoothedWorldPosition.TryGetValue(id, out Vector3 previous)
                    ? worldPosition
                    : Vector3.Lerp(previous, worldPosition, 1f - Mathf.Exp(-PositionSmoothingRate * Time.deltaTime));

                _smoothedWorldPosition[id] = smoothed;
                instance.transform.position = _floatingOrigin.WorldToRender(smoothed);

                UpdateCarriedBox(id, instance, person, groundOffset);
            }

            // Anyone no longer alive (removed or destroyed) also goes back to the pool.
            _toHide.Clear();
            foreach (EntityId id in _activeInstances.Keys)
            {
                if (!seen.Contains(id))
                {
                    _toHide.Add(id);
                }
            }

            foreach (EntityId id in _toHide)
            {
                HideInstance(id);
            }
        }

        /// <summary>R20a: the ground offset only needs measuring once per unit type - every instance of the same asset shares the same model and scale, so its bottom sits the same distance below its pivot every time.</summary>
        private float GroundOffsetFor(UnitAsset asset, GameObject instance)
        {
            if (!_groundOffsetByAsset.TryGetValue(asset, out float offset))
            {
                offset = GroundPlacement.PivotHeightAboveBase(instance);
                _groundOffsetByAsset[asset] = offset;
            }

            return offset;
        }

        /// <summary>R20a: shows a box from the carried item's stack models (stage 1, picked at random) in front of the person at about chest height while they carry something; rebuilt only when the carried item changes (a new "trip"), and removed the moment CarriedItems no longer holds anything (delivered, or eaten).</summary>
        private void UpdateCarriedBox(EntityId id, GameObject personInstance, PersonState person, float personGroundOffset)
        {
            string carriedItemId = FirstCarriedItemId(person);

            if (carriedItemId == null)
            {
                HideCarriedBox(id);
                return;
            }

            if (_carriedBoxItemId.TryGetValue(id, out string shownItemId) && shownItemId == carriedItemId)
            {
                return;
            }

            HideCarriedBox(id);

            if (!_itemAssetsById.TryGetValue(carriedItemId, out ItemAsset asset))
            {
                return;
            }

            GameObject box = BuildCarriedBoxVisual(asset);
            box.transform.SetParent(personInstance.transform, worldPositionStays: false);
            box.transform.localPosition = new Vector3(0f, CarriedBoxHeightAboveGroundMeters - personGroundOffset, CarriedBoxForwardMeters);
            box.transform.localRotation = Quaternion.identity;

            _carriedBoxInstances[id] = box;
            _carriedBoxItemId[id] = carriedItemId;
        }

        private GameObject BuildCarriedBoxVisual(ItemAsset asset)
        {
            GameObject model = PickRandomStage1Model(asset);
            GameObject box = model != null ? Object.Instantiate(model) : BuildPlaceholderBox();

            float scale = StockpileStackPresenter.BoxSizeMeters * (float)_settings.Get(CarriedBoxScaleSettingId);
            box.transform.localScale = new Vector3(scale, scale, scale);
            return box;
        }

        private static GameObject PickRandomStage1Model(ItemAsset asset)
        {
            IReadOnlyList<StackModelDefinition> models = asset.StackModels;
            return models.Count > 0 ? models[Random.Range(0, models.Count)].Stage1Model : null;
        }

        /// <summary>Until an item has a real stack model, carrying it shows this plain brown box - the same placeholder look as an empty stockpile stack's first box.</summary>
        private static GameObject BuildPlaceholderBox()
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(box.GetComponent<Collider>());

            var renderer = box.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var brown = new Color(0.45f, 0.3f, 0.15f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", brown);
            }
            else
            {
                material.color = brown;
            }

            renderer.sharedMaterial = material;
            return box;
        }

        private static string FirstCarriedItemId(PersonState person)
        {
            foreach (string itemId in person.CarriedItems.Keys)
            {
                return itemId;
            }

            return null;
        }

        private void HideCarriedBox(EntityId id)
        {
            if (_carriedBoxInstances.TryGetValue(id, out GameObject box))
            {
                Object.Destroy(box);
                _carriedBoxInstances.Remove(id);
            }

            _carriedBoxItemId.Remove(id);
        }

        private void HideInstance(EntityId id)
        {
            if (!_activeInstances.TryGetValue(id, out GameObject instance))
            {
                return;
            }

            instance.SetActive(false);
            _activeInstances.Remove(id);
            _entityByInstance.Remove(instance);
            _smoothedWorldPosition.Remove(id);
            _pool.Push(instance);

            HideCarriedBox(id);
        }
    }
}
