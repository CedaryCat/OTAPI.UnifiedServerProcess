using TrProtocol.Models.Interfaces;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct NPCHome : INetPacket, INPCSlot {
    public readonly MessageID Type => MessageID.NPCHome;
    public short NPCSlot { get; set; }
    public Point16 Position;
    public byte Homeless;
}