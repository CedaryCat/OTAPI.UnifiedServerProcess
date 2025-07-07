using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct FrameSection : INetPacket
{
    public readonly MessageID Type => MessageID.FrameSection;
    public Point16 Start { get; set; }
    public Point16 End { get; set; }
}
