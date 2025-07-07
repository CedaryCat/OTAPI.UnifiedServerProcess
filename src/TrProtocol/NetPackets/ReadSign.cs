using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ReadSign : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.ReadSign;
    public short SignSlot;
    public Point16 Position;
    public string Text;
    public byte PlayerSlot { get; set; }
    public byte Bit1;
}