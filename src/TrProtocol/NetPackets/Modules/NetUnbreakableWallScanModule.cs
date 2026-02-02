using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetUnbreakableWallScanModule : INetModulesPacket, IPlayerSlot
{
    public readonly NetModuleType ModuleType => NetModuleType.NetUnbreakableWallScanModule;

    public byte PlayerSlot { get; set; }
    public bool InsideUnbreakableWalls;
}
