using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct MurderSomeoneElsesProjectile : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.MurderSomeoneElsesProjectile;
    public byte OtherPlayerSlot { get; set; }
    public byte HighBitOfPlayerIsAlwaysZero = 0;
    public byte AI1;
}