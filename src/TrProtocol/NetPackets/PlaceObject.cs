using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct PlaceObject : INetPacket
{
    public readonly MessageID Type => MessageID.PlaceObject;
    public Point16 Position;
    public short ObjectType;
    public short Style;
    public byte Alternate;
    public sbyte Random;
    public bool Direction;
}