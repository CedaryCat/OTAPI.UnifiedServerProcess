using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCreativePowersModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativePowersModule;
    public CreativePower CreativePower;
}
