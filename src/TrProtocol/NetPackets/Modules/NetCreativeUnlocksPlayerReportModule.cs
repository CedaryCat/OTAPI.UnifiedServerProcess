using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCreativeUnlocksPlayerReportModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativeUnlocksPlayerReportModule;
    public byte AlwaysZero = 0;
    public short ItemId;
    public ushort Count;
}
