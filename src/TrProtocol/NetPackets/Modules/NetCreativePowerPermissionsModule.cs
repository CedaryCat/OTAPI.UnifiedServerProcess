using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetCreativePowerPermissionsModule : NetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativePowerPermissionsModule;
    public byte AlwaysZero = 0;
    public ushort PowerId;
    public byte Level;
}