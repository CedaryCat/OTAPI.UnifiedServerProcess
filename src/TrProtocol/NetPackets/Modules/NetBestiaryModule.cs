using TrProtocol.Attributes;
using TrProtocol.Models;
using Terraria.GameContent.NetModules;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetBestiaryModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetBestiaryModule;
    public NetBestiaryModule_BestiaryUnlockType UnlockType;
    public short NPCType;
    [ConditionEqual(nameof(UnlockType), 0)]
    public ushort KillCount;
}
