using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct SyncRevengeMarker : INetPacket {
    public readonly MessageID Type => MessageID.SyncRevengeMarker;
    public int ID;
    public Vector2 Position;
    public int NetID;
    public float Percent;
    public int NPCType;
    public int NPCAI;
    public int CoinValue;
    public float BaseValue;
    public bool SpawnFromStatue;
}