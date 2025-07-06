using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCreativePowersModule : NetModulesPacket, IExtraData
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativePowersModule;
    public CreativePowerTypes PowerType;
}
