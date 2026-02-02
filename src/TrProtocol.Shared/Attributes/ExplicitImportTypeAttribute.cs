namespace TrProtocol.Attributes;

/// <summary>
/// Declares an explicit type dependency for build-time tooling.
/// </summary>
/// <remarks>
/// This attribute is currently not referenced by <c>TrProtocol.SerializerGenerator</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class ExplicitImportTypeAttribute(Type type) : Attribute
{
    public Type Type = type;
}
