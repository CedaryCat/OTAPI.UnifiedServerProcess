using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct ChangeDoor : INetPacket {
    public readonly MessageID Type => MessageID.ChangeDoor;
    public bool ChangeType;
    public Point16 Position;
    public byte Direction;
}
