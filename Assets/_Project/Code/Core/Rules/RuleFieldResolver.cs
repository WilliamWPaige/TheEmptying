using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules
{
    /// <summary>Resolves a compiled field value to a RuleValue, reading a setting reference's live value each time. Shared by trigger activation and per-step input resolution.</summary>
    internal static class RuleFieldResolver
    {
        public static RuleValue Resolve(CompiledFieldValue field, SettingsRegistry settings)
        {
            if (field.IsSettingReference)
            {
                double raw = settings.Get(field.SettingId);
                return field.DataType switch
                {
                    RuleDataType.Number => RuleValue.Number(raw),
                    RuleDataType.Bool => RuleValue.Bool(raw != 0),
                    _ => RuleValue.Default(field.DataType)
                };
            }

            return RuleValue.FromLiteral(field.DataType, field.LiteralValue);
        }
    }
}
