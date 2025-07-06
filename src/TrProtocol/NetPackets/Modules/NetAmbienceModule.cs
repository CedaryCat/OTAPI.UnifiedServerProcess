using TrProtocol.Models;
using TrProtocol.Models.Interfaces;
using Terraria.GameContent.Ambience;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetAmbienceModule : NetModulesPacket, IPlayerSlot
{
    public readonly NetModuleType ModuleType => NetModuleType.NetAmbienceModule;
    public byte PlayerSlot { get; set; }
    public int Random;
    public SkyEntityType SkyType;
}
