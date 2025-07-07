using System.Diagnostics.CodeAnalysis;
using TrProtocol.Interfaces;

namespace TrProtocol.Models
{
    public partial struct ItemData : IEquatable<ItemData>, IAutoSerializable
    {
        public short ItemID;
        public byte Prefix;
        public short Stack;

        public readonly bool Equals(ItemData other) =>
            ItemID == other.ItemID && Prefix == other.Prefix && Stack == other.Stack;
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ItemData data && Equals(data);

        public readonly override int GetHashCode() => HashCode.Combine(ItemID, Prefix, Stack);

        public static bool operator ==(ItemData left, ItemData right) => left.Equals(right);

        public static bool operator !=(ItemData left, ItemData right) => !left.Equals(right);
    }
}
