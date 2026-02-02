using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets;


[PolymorphicBase(typeof(NetModuleType), nameof(ModuleType))]
[ImplementationClaim(MessageID.NetModules)]
public partial interface INetModulesPacket : INetPacket, IAutoSerializable
{
    public abstract NetModuleType ModuleType { get; }
}
