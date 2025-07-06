using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct CrystalInvasionStart : INetPacket {
    public readonly MessageID Type => MessageID.CrystalInvasionStart;
    public Point16 Position;
}