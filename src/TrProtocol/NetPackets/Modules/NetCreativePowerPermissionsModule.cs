using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetCreativePowerPermissionsModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativePowerPermissionsModule;
    public byte AlwaysZero = 0;
    public ushort PowerId;
    public byte Level;
}