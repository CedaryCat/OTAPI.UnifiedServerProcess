using System.Runtime.InteropServices;
using TrProtocol.Attributes;
using TrProtocol.Models;
using static Terraria.GameContent.BannerSystem.NetBannersModule;

namespace TrProtocol.NetPackets.Modules;

[StructLayout(LayoutKind.Explicit)]
public partial struct NetBannersModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetBannersModule;

    [FieldOffset(0)]
    public MessageType MessageType;

    [FieldOffset(2)]
    [ConditionNotEqual(nameof(MessageType), MessageType.FullState)]
    public short BannerID;

    [FieldOffset(4)]
    [ConditionEqual(nameof(MessageType), MessageType.KillCountUpdate)]
    public int KillCountUpdate;

    [FieldOffset(4)]
    [ConditionEqual(nameof(MessageType), MessageType.ClaimCountUpdate)]
    public ushort ClaimCountUpdate;

    [FieldOffset(4)]
    [ConditionEqual(nameof(MessageType), MessageType.ClaimRequest)]
    public BannersModuleClaimRequest ClaimRequest;

    [FieldOffset(4)]
    [ConditionEqual(nameof(MessageType), MessageType.ClaimResponse)]
    public BannersModuleClaimResponse ClaimResponse;

    [FieldOffset(8)]
    [InitDefaultValue]
    [ConditionEqual(nameof(MessageType), MessageType.FullState)]
    public BannersModuleFullState? FullState;
}
public class BannersModuleFullState
{
    public short KillCounterLength;
    [ArraySize(nameof(KillCounterLength))]
    public int[] KillCounter = [];

    public short ClaimableBannersLength;
    [ArraySize(nameof(ClaimableBannersLength))]
    public ushort[] ClaimableBanners = [];
}
public struct BannersModuleClaimRequest
{
    public ushort Amount;
}
public struct BannersModuleClaimResponse
{
    public ushort Amount;
    public bool Granted;
}
