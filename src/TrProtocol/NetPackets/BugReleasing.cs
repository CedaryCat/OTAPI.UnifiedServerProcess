using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct BugReleasing : INetPacket {
    public readonly MessageID Type => MessageID.BugReleasing;
    public Point Position;
    public short NPCType;
    public byte Styl;
}