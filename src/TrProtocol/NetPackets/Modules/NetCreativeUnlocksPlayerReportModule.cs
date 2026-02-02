using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetCreativeUnlocksPlayerReportModule : INetModulesPacket, IPlayerSlot
{
    public readonly NetModuleType ModuleType => NetModuleType.NetCreativeUnlocksPlayerReportModule;
    public byte PlayerSlot { get; set; }
    public ushort ItemId;
    public ushort Count;
}
