using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets {

    [PolymorphicBase(typeof(NetModuleType), nameof(ModuleType))]
    [ImplementationClaim(MessageID.NetModules)]
    public partial interface NetModulesPacket : INetPacket, IAutoSerializable {
        public abstract NetModuleType ModuleType { get; }
    }
}
