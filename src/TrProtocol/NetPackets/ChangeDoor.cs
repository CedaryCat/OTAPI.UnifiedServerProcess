using Terraria.DataStructures;
using TrProtocol.Models;

namespace TrProtocol.NetPackets;

public partial struct ChangeDoor : INetPacket
{
    public readonly MessageID Type => MessageID.ChangeDoor;
    public DoorAction ChangeType;
    public Point16 Position;
    public byte Direction;
}
