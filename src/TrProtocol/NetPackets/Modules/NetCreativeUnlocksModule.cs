using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetCreativeUnlocksModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativeUnlocksModule;
    public short ItemId;
    public ushort Count;
}
