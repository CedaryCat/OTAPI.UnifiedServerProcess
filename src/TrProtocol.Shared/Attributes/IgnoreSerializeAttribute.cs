namespace TrProtocol.Attributes;

/// <summary>
/// Excludes a member from source-generated serialization.
/// </summary>
/// <remarks>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. Members with this attribute are ignored during member expansion
/// and will not be read or written by generated code.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class IgnoreSerializeAttribute : Attribute { }
