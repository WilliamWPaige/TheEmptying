using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Saves
{
    /// <summary>Serializes one RuleValue: its DataType tag, then the matching payload.</summary>
    internal static class RuleValueSerializer
    {
        public static void Write(SaveWriter writer, RuleValue value)
        {
            writer.WriteInt((int)value.DataType);

            switch (value.DataType)
            {
                case RuleDataType.Number:
                    writer.WriteDouble(value.AsNumber);
                    break;
                case RuleDataType.Bool:
                    writer.WriteBool(value.AsBool);
                    break;
                case RuleDataType.Text:
                case RuleDataType.ContentId:
                    writer.WriteString(value.AsText);
                    break;
                case RuleDataType.Entity:
                    writer.WriteEntityId(value.AsEntity);
                    break;
            }
        }

        public static RuleValue Read(SaveReader reader)
        {
            var type = (RuleDataType)reader.ReadInt();

            switch (type)
            {
                case RuleDataType.Number:
                    return RuleValue.Number(reader.ReadDouble());
                case RuleDataType.Bool:
                    return RuleValue.Bool(reader.ReadBool());
                case RuleDataType.Text:
                    return RuleValue.Text(reader.ReadString());
                case RuleDataType.ContentId:
                    return RuleValue.ContentId(reader.ReadString());
                case RuleDataType.Entity:
                    return RuleValue.Entity(reader.ReadEntityId());
                default:
                    return RuleValue.Default(type);
            }
        }
    }
}
