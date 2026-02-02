using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

[PolymorphicBase(typeof(CreativePowerTypes), nameof(PowerType))]
public abstract partial class CreativePower : IAutoSerializable
{
    public abstract CreativePowerTypes PowerType { get; }
    public abstract unsafe void ReadContent(ref void* ptr);
    public abstract unsafe void WriteContent(ref void* ptr);
}
