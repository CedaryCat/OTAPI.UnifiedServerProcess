using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models;
using TrProtocol.NetPackets.Mobile;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCraftingRequestsModule : INetModulesPacket, ISideSpecific
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCraftingRequestsModule;
    [C2SOnly] public NetCraftingRequest CraftingRequest;
    [S2COnly] public bool RequestApproved;
}
