using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// A node's raw field value as loaded from content: either a literal JSON
    /// value or a reference to a named setting ({"setting": "&lt;id&gt;"}).
    /// Resolved against a block type's field definition at compile time.
    /// </summary>
    [JsonConverter(typeof(RuleFieldValueSourceConverter))]
    public sealed class RuleFieldValueSource
    {
        public bool IsSettingReference { get; }
        public string SettingId { get; }
        public JToken Literal { get; }

        private RuleFieldValueSource(bool isSettingReference, string settingId, JToken literal)
        {
            IsSettingReference = isSettingReference;
            SettingId = settingId;
            Literal = literal;
        }

        public static RuleFieldValueSource FromLiteral(JToken literal) => new RuleFieldValueSource(false, null, literal);

        public static RuleFieldValueSource FromSetting(string settingId) => new RuleFieldValueSource(true, settingId, null);
    }

    internal sealed class RuleFieldValueSourceConverter : JsonConverter<RuleFieldValueSource>
    {
        public override RuleFieldValueSource ReadJson(JsonReader reader, Type objectType, RuleFieldValueSource existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                if (obj.TryGetValue("setting", out JToken settingToken))
                {
                    return RuleFieldValueSource.FromSetting(settingToken.Value<string>());
                }
            }

            return RuleFieldValueSource.FromLiteral(token);
        }

        public override void WriteJson(JsonWriter writer, RuleFieldValueSource value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (value.IsSettingReference)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("setting");
                writer.WriteValue(value.SettingId);
                writer.WriteEndObject();
                return;
            }

            if (value.Literal == null)
            {
                writer.WriteNull();
            }
            else
            {
                value.Literal.WriteTo(writer);
            }
        }
    }
}
