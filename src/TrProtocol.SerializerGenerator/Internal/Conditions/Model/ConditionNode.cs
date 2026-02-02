namespace TrProtocol.SerializerGenerator.Internal.Conditions.Model;

/// <summary>
/// Base class for condition nodes, representing a node in the serialization condition tree.
/// </summary>
public abstract record ConditionNode
{
    /// <summary>
    /// Generates a C# boolean expression string for this condition.
    /// </summary>
    /// <param name="parentVar">The parent variable name (used for nested types).</param>
    /// <param name="isSerializing">Whether we're generating for the serialization direction (affects C2SOnly/S2COnly).</param>
    /// <returns>A C# boolean expression.</returns>
    public abstract string ToConditionExpression(string? parentVar, bool isSerializing);

    /// <summary>
    /// Gets a normalized key for comparing condition equivalence.
    /// Conditions with the same normalized key should be treated as equivalent.
    /// </summary>
    public abstract string GetNormalizedKey();

    /// <summary>
    /// Determines whether this condition logically contains another condition.
    /// I.e. if this condition is true, then the other condition must also be true.
    /// </summary>
    public virtual bool Contains(ConditionNode other)
    {
        return GetNormalizedKey() == other.GetNormalizedKey();
    }

    /// <summary>
    /// Indicates whether this condition depends on direction (C2SOnly/S2COnly).
    /// </summary>
    public virtual bool IsDirectionDependent => false;

    /// <summary>
    /// Indicates whether this is an empty condition (always true).
    /// </summary>
    public virtual bool IsEmpty => false;
}

/// <summary>
/// Empty condition node (always true).
/// </summary>
public sealed record EmptyConditionNode : ConditionNode
{
    public static readonly EmptyConditionNode Instance = new();

    private EmptyConditionNode() { }

    public override string ToConditionExpression(string? parentVar, bool isSerializing) => "true";
    public override string GetNormalizedKey() => "EMPTY";
    public override bool IsEmpty => true;
    public override bool Contains(ConditionNode other) => true; // An empty condition contains all conditions.
}

/// <summary>
/// Boolean member condition node (e.g. [Condition(nameof(HasFlag))]).
/// </summary>
public sealed record BooleanConditionNode(string MemberName, bool ExpectedValue = true) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var memberAccess = parentVar is null ? MemberName : $"{parentVar}.{MemberName}";
        return ExpectedValue ? memberAccess : $"!{memberAccess}";
    }

    public override string GetNormalizedKey() => $"BOOL:{MemberName}:{ExpectedValue}";
}

/// <summary>
/// BitsByte index condition node (e.g. [Condition(nameof(Flags), 0)]).
/// </summary>
public sealed record BitsByteConditionNode(string MemberName, string Index, bool ExpectedValue = true) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var memberAccess = parentVar is null ? MemberName : $"{parentVar}.{MemberName}";
        var expression = $"{memberAccess}[{Index}]";
        return ExpectedValue ? expression : $"!{expression}";
    }

    public override string GetNormalizedKey() => $"BITS:{MemberName}[{Index}]:{ExpectedValue}";
}

/// <summary>
/// Comparison condition node (e.g. [ConditionEqual(nameof(Type), 5)]).
/// </summary>
public sealed record ComparisonConditionNode(
    string LeftExpression,
    string Operator,
    string RightExpression,
    string? CastType = null) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var left = parentVar is null ? LeftExpression : $"{parentVar}.{LeftExpression}";
        var cast = CastType is null ? "" : $"({CastType})";
        return $"{left} {Operator} {cast}{RightExpression}";
    }

    public override string GetNormalizedKey() => $"CMP:{LeftExpression}{Operator}{RightExpression}";
}

/// <summary>
/// Lookup condition node (e.g. [ConditionLookupMatch(nameof(Table), nameof(Key))]).
/// </summary>
public sealed record LookupConditionNode(
    string TableName,
    string KeyMemberName,
    bool ExpectedValue = true) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var keyAccess = parentVar is null ? KeyMemberName : $"{parentVar}.{KeyMemberName}";
        var expression = $"{TableName}[{keyAccess}]";
        return ExpectedValue ? expression : $"!{expression}";
    }

    public override string GetNormalizedKey() => $"LOOKUP:{TableName}[{KeyMemberName}]:{ExpectedValue}";
}

/// <summary>
/// Lookup comparison condition node (e.g. [ConditionLookupEqual(nameof(Table), nameof(Key), 5)]).
/// </summary>
public sealed record LookupComparisonConditionNode(
    string TableName,
    string KeyMemberName,
    string Operator,
    string RightExpression) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var keyAccess = parentVar is null ? KeyMemberName : $"{parentVar}.{KeyMemberName}";
        return $"{TableName}[{keyAccess}] {Operator} {RightExpression}";
    }

    public override string GetNormalizedKey() => $"LOOKUP_CMP:{TableName}[{KeyMemberName}]{Operator}{RightExpression}";
}

/// <summary>
/// Side-specific condition node (C2SOnly/S2COnly).
/// </summary>
public sealed record SideConditionNode(bool IsC2SOnly) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        // C2SOnly: serialize => !IsServerSide; deserialize => IsServerSide
        // S2COnly: serialize => IsServerSide;  deserialize => !IsServerSide
        var needNegate = IsC2SOnly ? isSerializing : !isSerializing;
        return needNegate ? "!IsServerSide" : "IsServerSide";
    }

    public override string GetNormalizedKey() => IsC2SOnly ? "SIDE:C2S" : "SIDE:S2C";
    public override bool IsDirectionDependent => true;
}

/// <summary>
/// Array index condition node (e.g. [ConditionArray(nameof(Flags), 0)]).
/// </summary>
public sealed record ArrayIndexConditionNode(
    string MemberName,
    string IndexOffset,
    string IndexVariable,
    bool ExpectedValue = true) : ConditionNode
{
    public override string ToConditionExpression(string? parentVar, bool isSerializing)
    {
        var memberAccess = parentVar is null ? MemberName : $"{parentVar}.{MemberName}";
        var indexExpr = IndexOffset == "0" ? IndexVariable : $"{IndexVariable} + {IndexOffset}";
        var expression = $"{memberAccess}[{indexExpr}]";
        return ExpectedValue ? expression : $"!{expression}";
    }

    public override string GetNormalizedKey() => $"ARRAY:{MemberName}[{IndexVariable}+{IndexOffset}]:{ExpectedValue}";
}
