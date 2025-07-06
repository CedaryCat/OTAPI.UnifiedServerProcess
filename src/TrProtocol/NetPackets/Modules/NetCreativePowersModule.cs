using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCreativePowersModule : INetModulesPacket, IExtraData
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativePowersModule;
    public CreativePowerTypes PowerType;
}
