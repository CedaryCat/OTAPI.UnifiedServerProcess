using TrProtocol.Models;

namespace TrProtocol.NetPackets;

public partial struct FishOutNPC : INetPacket
{
    public readonly MessageID Type => MessageID.FishOutNPC;
    public PointU16 Position;
    public short Start;
}