using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncTilePicking : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncTilePicking;
    public byte PlayerSlot { get; set; }
    public Point16 Position;
    public byte Damage;
}