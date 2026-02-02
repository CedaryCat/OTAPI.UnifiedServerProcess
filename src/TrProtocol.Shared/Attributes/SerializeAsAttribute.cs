namespace TrProtocol.Attributes;

/// <summary>
/// Instructs the source generator to serialize a primitive numeric member as another numeric type.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> for primitive numeric members. Generated code casts the member to
/// <see cref="TargetType"/> when writing, and casts back when reading.
/// </para>
/// <para>
/// This is typically used to control the wire size without changing the in-memory type.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class SerializeAsAttribute : Attribute
{
    public Type TargetType;
    public SerializeAsAttribute(Type numberType) {
        TargetType = numberType;
    }
}
