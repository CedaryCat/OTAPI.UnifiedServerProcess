using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetLiquidModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetLiquidModule;
    public LiquidData LiquidChanges;
}
