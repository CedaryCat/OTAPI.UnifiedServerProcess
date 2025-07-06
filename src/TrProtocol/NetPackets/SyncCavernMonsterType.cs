using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct SyncCavernMonsterType : INetPacket {
    public readonly MessageID Type => MessageID.SyncCavernMonsterType;
    [ArraySize(6)]
    public short[] CavenMonsterType;
}