using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct AddPlayerBuff : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.AddPlayerBuff;
    public byte OtherPlayerSlot { get; set; }
    public ushort BuffType;
    public int BuffTime;
}
