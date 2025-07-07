using TrProtocol.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncEmoteBubble : INetPacket, IExtraData
{
    public readonly MessageID Type => MessageID.SyncEmoteBubble;
    public int ID;
    public byte EmoteType;
}