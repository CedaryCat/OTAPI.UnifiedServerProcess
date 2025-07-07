using Terraria.GameContent.NetModules;
using TrProtocol.Attributes;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;
public partial struct NetBestiaryModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetBestiaryModule;
    public NetBestiaryModule_BestiaryUnlockType UnlockType;
    public short NPCType;
    [ConditionEqual(nameof(UnlockType), 0)]
    public ushort KillCount;
}
