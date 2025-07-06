using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct PlayerHealth : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.PlayerHealth;
    public byte PlayerSlot { get; set; }
    public short StatLife;
    public short StatLifeMax;
}
