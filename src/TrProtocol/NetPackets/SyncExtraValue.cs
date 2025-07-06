using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct SyncExtraValue : INetPacket {
    public readonly MessageID Type => MessageID.SyncExtraValue;
    public short NPCSlot;
    public int Extra;
    public Vector2 MoneyPing;
}