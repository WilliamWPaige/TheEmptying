using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Modules.People;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>R8: an info window selection for one person - name, role/sex/age, specialty (highlighted), attributes, then skills grouped.</summary>
    public sealed class PersonSelectionTarget : ISelectionTarget
    {
        private readonly PeopleModule _peopleModule;
        private readonly ContentDatabase _content;
        private readonly EntityId _id;

        public PersonSelectionTarget(PeopleModule peopleModule, ContentDatabase content, EntityId id)
        {
            _peopleModule = peopleModule;
            _content = content;
            _id = id;
        }

        public EntityId Id => _id;

        public Vector2 Footprint => Vector2.one;

        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            if (_peopleModule.TryGetPerson(_id, out PersonState person))
            {
                worldPosition = new Vector3((float)person.WorldX, 0f, (float)person.WorldZ);
                return true;
            }

            worldPosition = default;
            return false;
        }

        public void BuildContent(VisualElement container)
        {
            if (!_peopleModule.TryGetPerson(_id, out PersonState person))
            {
                return;
            }

            container.Add(new Label(person.Name)
            {
                style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });
            container.Add(new Label($"{person.Role} · {person.Sex} · {person.AgeYears} years")
            {
                style = { color = new Color(0.8f, 0.8f, 0.8f), marginBottom = 10 }
            });

            if (!string.IsNullOrEmpty(person.SpecialtySkillId))
            {
                container.Add(new Label($"Specialty: {DisplayNameForSkill(person.SpecialtySkillId)}")
                {
                    style = { color = new Color(1f, 0.85f, 0.3f), unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 10 }
                });
            }

            container.Add(SectionHeading("Attributes"));
            foreach (AttributeDefinition attribute in BuiltInContentTypes.OrderedAttributes(_content))
            {
                int value = person.Attributes.TryGetValue(attribute.Id, out int v) ? v : 0;
                container.Add(StatRow(attribute.DisplayName, value, false));
            }

            container.Add(SectionHeading("Skills"));
            foreach (System.Linq.IGrouping<string, SkillDefinition> group in BuiltInContentTypes.OrderedSkills(_content).GroupBy(s => s.Group))
            {
                container.Add(GroupHeading(group.Key));
                foreach (SkillDefinition skill in group)
                {
                    int value = person.Skills.TryGetValue(skill.Id, out int v) ? v : 0;
                    container.Add(StatRow(skill.DisplayName, value, skill.Id == person.SpecialtySkillId));
                }
            }
        }

        private string DisplayNameForSkill(string skillId)
        {
            SkillDefinition skill = BuiltInContentTypes.OrderedSkills(_content).FirstOrDefault(s => s.Id == skillId);
            return skill?.DisplayName ?? skillId;
        }

        private static Label SectionHeading(string text) => new Label(text)
        {
            style = { color = Color.white, fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 4 }
        };

        private static Label GroupHeading(string text) => new Label(text)
        {
            style = { color = new Color(0.75f, 0.75f, 0.75f), unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 2, marginLeft = 4 }
        };

        private static VisualElement StatRow(string label, int value, bool highlighted)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginLeft = 8, marginBottom = 1 } };
            Color color = highlighted ? new Color(1f, 0.85f, 0.3f) : Color.white;
            row.Add(new Label(label) { style = { color = color } });
            row.Add(new Label(value.ToString()) { style = { color = color } });
            return row;
        }
    }
}
