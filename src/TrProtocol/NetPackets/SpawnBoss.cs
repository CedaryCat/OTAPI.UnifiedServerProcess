using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SpawnBoss : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.SpawnBoss;
    public byte OtherPlayerSlot { get; set; }
    public byte HighBitOfPlayerIsAlwaysZero = 0;
    public short NPCType;
}