using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct NebulaLevelupRequest : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.NebulaLevelupRequest;
    public byte PlayerSlot { get; set; }
    public ushort NebulaType;
    public Vector2 Position;
}