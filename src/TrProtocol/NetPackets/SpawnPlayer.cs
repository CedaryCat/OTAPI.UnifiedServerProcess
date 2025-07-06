using TrProtocol.Models.Interfaces;
using Terraria;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct SpawnPlayer : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.SpawnPlayer;
    public byte PlayerSlot { get; set; }
    public Point16 Position;
    public int Timer;
    public short DeathsPVE;
    public short DeathsPVP;
    public PlayerSpawnContext Context;
}
