using Terraria.GameContent.Ambience;
using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetAmbienceModule : INetModulesPacket, IPlayerSlot
{
    public readonly NetModuleType ModuleType => NetModuleType.NetAmbienceModule;
    public byte PlayerSlot { get; set; }
    public int Random;
    public SkyEntityType SkyType;
}
