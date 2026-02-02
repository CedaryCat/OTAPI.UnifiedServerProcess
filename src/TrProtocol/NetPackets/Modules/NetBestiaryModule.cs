using TrProtocol.Attributes;
using TrProtocol.Models;
using static Terraria.GameContent.NetModules.NetBestiaryModule;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetBestiaryModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetBestiaryModule;
    public BestiaryUnlockType UnlockType;
    public short NPCType;
    [ConditionEqual(nameof(UnlockType), 0)]
    [Int7BitEncoded]
    public int KillCount;


}
