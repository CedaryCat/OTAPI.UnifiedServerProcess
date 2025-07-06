using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct FoodPlatterTryPlacing : INetPacket {
    public readonly MessageID Type => MessageID.FoodPlatterTryPlacing;
    public Point16 Position;
    public short ItemType;
    public byte Prefix;
    public short Stack;
}