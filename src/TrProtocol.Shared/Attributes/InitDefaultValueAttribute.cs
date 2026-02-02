namespace TrProtocol.Attributes;

/// <summary>
/// Indicates that a member may be initialized to its default value by the source generator when generating constructors.
/// </summary>
/// <remarks>
/// <para>
/// Used by <c>TrProtocol.SerializerGenerator</c> when generating constructors for types participating in automatic
/// serialization. When possible (e.g. nullable or unmanaged members), the generator may omit such members from the
/// required constructor parameters and use <c>default</c> initialization instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class InitDefaultValueAttribute : Attribute
{
}
