using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets;

public partial struct TileSection : INetPacket, ILengthAware
{
    public readonly MessageID Type => MessageID.TileSection;
    public SectionData Data;
}
