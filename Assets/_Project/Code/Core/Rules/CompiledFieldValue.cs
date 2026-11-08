namespace TheEmptying.Core.Rules
{
    /// <summary>A node's resolved field value: either a literal of the field's RuleDataType, or a reference to a named setting.</summary>
    public sealed class CompiledFieldValue
    {
        public RuleDataType DataType { get; }
        public bool IsSettingReference { get; }
        public string SettingId { get; }
        public object LiteralValue { get; }

        private CompiledFieldValue(RuleDataType dataType, bool isSettingReference, string settingId, object literalValue)
        {
            DataType = dataType;
            IsSettingReference = isSettingReference;
            SettingId = settingId;
            LiteralValue = literalValue;
        }

        public static CompiledFieldValue FromLiteral(RuleDataType dataType, object literalValue) => new CompiledFieldValue(dataType, false, null, literalValue);

        public static CompiledFieldValue FromSetting(RuleDataType dataType, string settingId) => new CompiledFieldValue(dataType, true, settingId, null);
    }
}
