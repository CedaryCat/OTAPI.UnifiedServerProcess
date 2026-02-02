using Terraria.DataStructures;
using TrProtocol.Models;

namespace TrProtocol.NetPackets;

public partial struct Unlock : INetPacket
{
    public readonly MessageID Type => MessageID.Unlock;
    public LockAction UnlockType;
    public Point16 Position;
}
