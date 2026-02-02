namespace TrProtocol.Attributes;

/// <summary>
/// Marker attribute reserved for conditional serialization scenarios.
/// </summary>
/// <remarks>
/// This attribute is currently not referenced by <c>TrProtocol.SerializerGenerator</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionIgnoreSerializeAttribute : Attribute { }
