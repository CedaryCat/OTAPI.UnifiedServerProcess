using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct LandGolfBallInCup : INetPacket, IOtherPlayerSlot {
    public readonly MessageID Type => MessageID.LandGolfBallInCup;
    public byte OtherPlayerSlot { get; set; }
    public PointU16 Position;
    public ushort Hits;
    public ushort ProjType;
}