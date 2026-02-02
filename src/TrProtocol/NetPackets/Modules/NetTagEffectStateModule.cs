using System.Runtime.InteropServices;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models;
using TrProtocol.Models.Interfaces;
using static Terraria.GameContent.Items.TagEffectState.NetModule;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetTagEffectStateModule : INetModulesPacket, ISideSpecific, IPlayerSlot
{
    public readonly NetModuleType ModuleType => NetModuleType.NetTagEffectStateModule;
    public byte PlayerSlot { get; set; }
    public MessageType MessageType;
    
    [ConditionEqual(nameof(MessageType), MessageType.ChangeActiveEffect)]
    public short ChangeActiveType;

    public readonly bool IsNPCChange => MessageType is
        MessageType.ApplyTagToNPC or
        MessageType.EnableProcOnNPC or
        MessageType.ClearProcOnNPC;

    [Condition(nameof(IsNPCChange))]
    public byte NPCSlot;

    [ConditionEqual(nameof(MessageType), MessageType.FullState), S2COnly]
    public NetTagEffectFullState? FullState;
}

public partial class NetTagEffectFullState
{
    public short Type;
    [SparseArray(200)]
    public int[] TimeLeftOnNPC = [];
    public bool SyncProcs => false;
    [Condition(nameof(SyncProcs))]
    [SparseArray(200)]
    public int[]? ProcTimeLeftOnNPC;
}
