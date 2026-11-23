using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.People;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>R11: an info window selection for a placed building - its name, footprint, and how many people are inside right now, listed by name.</summary>
    public sealed class BuildingSelectionTarget : ISelectionTarget
    {
        private readonly BuildingsModule _buildingsModule;
        private readonly PeopleModule _peopleModule;
        private readonly ContentDatabase _content;
        private readonly EntityId _id;

        public BuildingSelectionTarget(BuildingsModule buildingsModule, PeopleModule peopleModule, ContentDatabase content, EntityId id)
        {
            _buildingsModule = buildingsModule;
            _peopleModule = peopleModule;
            _content = content;
            _id = id;
        }

        public Vector2 Footprint
        {
            get
            {
                return _buildingsModule.TryGetBuilding(_id, out BuildingState building)
                    ? new Vector2(building.FootprintWidth, building.FootprintDepth)
                    : Vector2.one;
            }
        }

        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            if (_buildingsModule.TryGetBuilding(_id, out BuildingState building))
            {
                worldPosition = new Vector3((float)building.WorldX, 0f, (float)building.WorldZ);
                return true;
            }

            worldPosition = default;
            return false;
        }

        public void BuildContent(VisualElement container)
        {
            if (!_buildingsModule.TryGetBuilding(_id, out BuildingState building))
            {
                return;
            }

            container.Add(new Label(DisplayNameFor(building.ContentId))
            {
                style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });
            container.Add(new Label($"Footprint: {building.FootprintWidth} x {building.FootprintDepth}")
            {
                style = { color = new Color(0.8f, 0.8f, 0.8f), marginBottom = 10 }
            });

            PersonState[] inside = _peopleModule.GetPeopleInside(_id).ToArray();
            container.Add(new Label($"People inside: {inside.Length}")
            {
                style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });

            foreach (PersonState person in inside)
            {
                container.Add(new Label(person.Name) { style = { color = new Color(0.85f, 0.85f, 0.85f), marginLeft = 8 } });
            }
        }

        private string DisplayNameFor(string contentId)
        {
            if (_content.TryGet(BuiltInContentTypes.BuildingTypeName, contentId, out ContentEntry entry) && entry.Definition is BuildingContent building)
            {
                return building.DisplayName;
            }

            return contentId;
        }
    }
}
