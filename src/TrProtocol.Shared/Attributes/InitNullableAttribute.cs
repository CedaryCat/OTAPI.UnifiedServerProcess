namespace TrProtocol.Attributes;

/// <summary>
/// Marks a member as allowed to remain null after source-generated constructors run.
/// </summary>
/// <remarks>
/// <para>
/// Used by <c>TrProtocol.SerializerGenerator</c> when generating constructors for automatically-serializable types.
/// Members decorated with this attribute are ignored when computing required constructor parameters.
/// </para>
/// <para>
/// For reference types, you should typically also declare the member as nullable in C# to match the intended contract.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class InitNullableAttribute : Attribute
{
}
