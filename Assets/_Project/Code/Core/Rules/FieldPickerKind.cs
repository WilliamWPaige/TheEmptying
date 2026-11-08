namespace TheEmptying.Core.Rules
{
    /// <summary>Editor hint: what kind of searchable picker a field's value should offer, beyond a plain typed value. Metadata only - the compiler ignores it except where it already checks values.</summary>
    public enum FieldPickerKind
    {
        None,
        EventId,
        SettingId,
        ContentId
    }
}
