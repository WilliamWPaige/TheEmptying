using System;

namespace TheEmptying.Core.Content
{
    /// <summary>A content type: its data-file type name, its C# class, and an optional per-item validator.</summary>
    public sealed class ContentTypeDefinition
    {
        public string TypeName { get; }
        public Type ClrType { get; }
        public Action<ContentDefinition, ContentValidationContext> Validator { get; }

        public ContentTypeDefinition(string typeName, Type clrType, Action<ContentDefinition, ContentValidationContext> validator = null)
        {
            TypeName = typeName;
            ClrType = clrType;
            Validator = validator;
        }
    }
}
