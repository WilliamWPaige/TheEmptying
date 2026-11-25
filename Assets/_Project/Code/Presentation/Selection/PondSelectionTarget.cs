using TheEmptying.Modules.Ponds;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>R17: an info window selection for one pond - drinks left out of the max, and whether it's dry.</summary>
    public sealed class PondSelectionTarget : ISelectionTarget
    {
        private readonly PondsModule _pondsModule;
        private readonly EntityId _id;

        public PondSelectionTarget(PondsModule pondsModule, EntityId id)
        {
            _pondsModule = pondsModule;
            _id = id;
        }

        public EntityId Id => _id;

        public Vector2 Footprint => _pondsModule.TryGetPond(_id, out PondState pond) ? new Vector2((float)(pond.RadiusMeters * 2.0), (float)(pond.RadiusMeters * 2.0)) : new Vector2(1f, 1f);

        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            if (_pondsModule.TryGetPond(_id, out PondState pond))
            {
                worldPosition = new Vector3((float)pond.CenterX, 0f, (float)pond.CenterZ);
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        public void BuildContent(VisualElement container)
        {
            if (!_pondsModule.TryGetPond(_id, out PondState pond))
            {
                return;
            }

            container.Add(new Label("Pond")
            {
                style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 10 }
            });

            int maxDrinks = (int)_pondsModule.MaxDrinksSetting;
            container.Add(new Label($"Drinks: {pond.DrinksCurrent} / {maxDrinks}") { style = { color = Color.white, marginBottom = 4 } });
            container.Add(new Label(pond.IsDry ? "Dry." : "Full of water.") { style = { color = new Color(0.8f, 0.8f, 0.8f) } });
        }
    }
}
