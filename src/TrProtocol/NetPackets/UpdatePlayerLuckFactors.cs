using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct UpdatePlayerLuckFactors : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.UpdatePlayerLuckFactors;
    public byte PlayerSlot { get; set; }
    public int LadyBugTime;
    public float Torch;
    public byte Potion;
    public bool HasGardenGnomeNearby;
    public float Equip;
    public float Coin;
}