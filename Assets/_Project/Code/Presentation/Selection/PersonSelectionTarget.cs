using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Timekeeping;
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

            BuildHungerSection(container, person);
            BuildActionSection(container, person);
            BuildCarryingAndStashSection(container, person);
            BuildLikesAndDislikesSection(container, person);
            BuildHistorySection(container, person);

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
                    container.Add(BuildSkillProgressBar(person, skill.Id));
                }
            }
        }

        /// <summary>R13: 100 (full) to 0 (starving), a simple bar - green while fed, red once below the eat threshold.</summary>
        private void BuildHungerSection(VisualElement container, PersonState person)
        {
            container.Add(SectionHeading("Hunger"));

            double fraction = System.Math.Clamp(person.Hunger / 100.0, 0.0, 1.0);
            bool hungry = person.Hunger < _peopleModule.HungerEatThreshold;

            var outer = new VisualElement
            {
                style = { height = 10, marginLeft = 8, marginBottom = 4, backgroundColor = new Color(1f, 1f, 1f, 0.15f) }
            };
            outer.Add(new VisualElement
            {
                style =
                {
                    height = 10,
                    width = Length.Percent((float)(fraction * 100.0)),
                    backgroundColor = hungry ? new Color(0.85f, 0.25f, 0.25f) : new Color(0.4f, 0.8f, 0.3f)
                }
            });
            container.Add(outer);
            container.Add(new Label($"{person.Hunger:0} / 100") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginLeft = 8, marginBottom = 10 } });
        }

        /// <summary>R13: what they're currently carrying (foraged for the stockpile), and what's in their home stash.</summary>
        private void BuildCarryingAndStashSection(VisualElement container, PersonState person)
        {
            container.Add(SectionHeading("Carrying"));
            container.Add(new Label(person.CarriedAmount > 0 ? $"Carrying {person.CarriedAmount} {DisplayNameForItem(person.CarriedItemId)}" : "(nothing)")
            {
                style = { color = new Color(0.85f, 0.85f, 0.85f), marginLeft = 8, marginBottom = 10 }
            });

            container.Add(SectionHeading("Home stash"));
            if (person.HomeStash.Count == 0)
            {
                container.Add(new Label("(empty)") { style = { color = new Color(0.6f, 0.6f, 0.6f), marginLeft = 8, marginBottom = 10 } });
            }
            else
            {
                foreach (System.Collections.Generic.KeyValuePair<string, int> entry in person.HomeStash)
                {
                    container.Add(new Label($"{DisplayNameForItem(entry.Key)}: {entry.Value}") { style = { color = new Color(0.85f, 0.85f, 0.85f), marginLeft = 8, marginBottom = 2 } });
                }

                container.Add(new VisualElement { style = { marginBottom = 8 } });
            }
        }

        /// <summary>R13: the kinds of work this person likes and dislikes, rolled once at creation.</summary>
        private void BuildLikesAndDislikesSection(VisualElement container, PersonState person)
        {
            container.Add(SectionHeading("Likes and dislikes"));

            string liked = person.LikedSkillIds.Count == 0 ? "(none)" : string.Join(", ", person.LikedSkillIds.Select(DisplayNameForSkill));
            string disliked = person.DislikedSkillIds.Count == 0 ? "(none)" : string.Join(", ", person.DislikedSkillIds.Select(DisplayNameForSkill));

            container.Add(new Label($"Likes: {liked}") { style = { color = new Color(0.5f, 0.85f, 0.5f), marginLeft = 8, whiteSpace = WhiteSpace.Normal } });
            container.Add(new Label($"Dislikes: {disliked}") { style = { color = new Color(0.85f, 0.55f, 0.5f), marginLeft = 8, marginBottom = 10, whiteSpace = WhiteSpace.Normal } });
        }

        /// <summary>R13: a small bar showing progress toward this skill's next level; empty (and unlabeled) once it's already at the cap.</summary>
        private VisualElement BuildSkillProgressBar(PersonState person, string skillId)
        {
            (double accumulated, double needed) = _peopleModule.GetSkillProgress(person, skillId);

            var outer = new VisualElement
            {
                style = { height = 4, marginLeft = 8, marginBottom = 3, backgroundColor = new Color(1f, 1f, 1f, 0.12f) }
            };

            if (needed > 0)
            {
                double fraction = System.Math.Clamp(accumulated / needed, 0.0, 1.0);
                outer.Add(new VisualElement
                {
                    style = { height = 4, width = Length.Percent((float)(fraction * 100.0)), backgroundColor = new Color(0.6f, 0.6f, 0.9f) }
                });
            }

            return outer;
        }

        private string DisplayNameForItem(string itemContentId)
        {
            if (string.IsNullOrEmpty(itemContentId))
            {
                return string.Empty;
            }

            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.DisplayName;
            }

            return itemContentId;
        }

        /// <summary>R10: current action, a progress bar, the target and distance (Walk only) and the thought line saying why.</summary>
        private static void BuildActionSection(VisualElement container, PersonState person)
        {
            container.Add(SectionHeading("Action"));

            PersonAction action = person.CurrentAction;
            if (action == null)
            {
                return;
            }

            container.Add(new Label(ActionKindLabel(action.Kind)) { style = { color = Color.white, marginLeft = 8 } });

            var progressOuter = new VisualElement
            {
                style =
                {
                    height = 8,
                    marginLeft = 8,
                    marginTop = 2,
                    marginBottom = 4,
                    backgroundColor = new Color(1f, 1f, 1f, 0.15f)
                }
            };
            var progressFill = new VisualElement
            {
                style =
                {
                    height = 8,
                    width = Length.Percent((float)(System.Math.Clamp(action.Progress, 0.0, 1.0) * 100.0)),
                    backgroundColor = new Color(0.3f, 0.75f, 1f)
                }
            };
            progressOuter.Add(progressFill);
            container.Add(progressOuter);

            if (action.Kind == ActionKind.Walk)
            {
                double distanceRemaining = System.Math.Max(0.0, action.TotalDistanceMeters - action.DistanceTraveledMeters);
                container.Add(new Label($"Target: cell ({action.TargetCell.X}, {action.TargetCell.Z}) - {distanceRemaining:0.#} m away")
                {
                    style = { color = new Color(0.8f, 0.8f, 0.8f), marginLeft = 8, whiteSpace = WhiteSpace.Normal }
                });
            }

            container.Add(new Label($"\"{action.Thought}\"")
            {
                style = { color = new Color(0.7f, 0.7f, 0.7f), unityFontStyleAndWeight = FontStyle.Italic, marginLeft = 8, marginBottom = 10, whiteSpace = WhiteSpace.Normal }
            });
        }

        /// <summary>R10: the last few finished actions, most recent first, with their in-game start time and how long they lasted.</summary>
        private static void BuildHistorySection(VisualElement container, PersonState person)
        {
            container.Add(SectionHeading("History"));

            if (person.History.Count == 0)
            {
                container.Add(new Label("(nothing yet)") { style = { color = new Color(0.6f, 0.6f, 0.6f), marginLeft = 8, marginBottom = 10 } });
                return;
            }

            foreach (PersonActionHistoryEntry entry in person.History)
            {
                GameDate date = GameDate.FromTick(entry.StartTick);
                double durationMinutes = entry.DurationTicks / (double)CalendarConstants.TicksPerMinute;
                container.Add(new Label($"{ActionKindLabel(entry.Kind)}: \"{entry.Thought}\" - {date}, {date.ToClockString(false)} ({durationMinutes:0.#} min)")
                {
                    style = { color = new Color(0.75f, 0.75f, 0.75f), marginLeft = 8, marginBottom = 2, whiteSpace = WhiteSpace.Normal }
                });
            }

            container.Add(new VisualElement { style = { marginBottom = 8 } });
        }

        private static string ActionKindLabel(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.Walk: return "Walking";
                case ActionKind.InsideBuilding: return "Inside";
                case ActionKind.Eating: return "Eating";
                case ActionKind.Digging: return "Digging";
                default: return "Idle";
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
