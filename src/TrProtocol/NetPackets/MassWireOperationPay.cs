using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct MassWireOperationPay : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.MassWireOperationPay;
    public short ItemType;
    public short Stack;
    public byte PlayerSlot { get; set; }
}