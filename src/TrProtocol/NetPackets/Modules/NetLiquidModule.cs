using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetLiquidModule : NetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetLiquidModule;
    public LiquidData LiquidChanges;
}
