namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes a member when a lookup-table expression evaluates to true.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. The generator emits an expression like
/// <c>LookupTable[Key]</c>, where <paramref name="lookupTable"/> and <paramref name="lookupKeyField"/> are used as
/// identifiers (typically supplied via <c>nameof(...)</c>).
/// </para>
/// <para>
/// This assumes <paramref name="lookupTable"/> is accessible in the generated context and has an indexer returning a
/// boolean value.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionLookupMatchAttribute : Attribute
{
    public string LookupTable;
    public string LookupKeyField;
    public ConditionLookupMatchAttribute(string lookupTable, string lookupKeyField) {
        LookupTable = lookupTable;
        LookupKeyField = lookupKeyField;
    }
}

/// <summary>
/// Conditionally serializes a member when a lookup-table expression evaluates to false.
/// </summary>
/// <remarks>See <see cref="ConditionLookupMatchAttribute"/> for the generated expression shape.</remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionLookupNotMatchAttribute : Attribute
{
    public string LookupTable;
    public string LookupKeyField;
    public ConditionLookupNotMatchAttribute(string lookupTable, string lookupKeyField) {
        LookupTable = lookupTable;
        LookupKeyField = lookupKeyField;
    }
}

/// <summary>
/// Conditionally serializes a member when a lookup-table expression equals a value.
/// </summary>
/// <remarks>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> as <c>LookupTable[Key] == Predicate</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionLookupEqualAttribute : Attribute
{
    public string LookupTable;
    public string LookupKeyField;
    public object Predicate;
    public ConditionLookupEqualAttribute(string lookupTable, string lookupKeyField, object pred) {
        LookupTable = lookupTable;
        LookupKeyField = lookupKeyField;
        Predicate = pred;
    }
}

/// <summary>
/// Conditionally serializes a member when a lookup-table expression does not equal a value.
/// </summary>
/// <remarks>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> as <c>LookupTable[Key] != Predicate</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionLookupNotEqualAttribute : Attribute
{
    public string LookupTable;
    public string LookupKeyField;
    public object Predicate;
    public ConditionLookupNotEqualAttribute(string lookupTable, string lookupKeyField, object pred) {
        LookupTable = lookupTable;
        LookupKeyField = lookupKeyField;
        Predicate = pred;
    }
}
