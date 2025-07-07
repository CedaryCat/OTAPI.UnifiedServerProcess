using TrProtocol.Attributes;
using TrProtocol.Models.TileEntities;

namespace TrProtocol.NetPackets;

public partial struct TileEntitySharing : INetPacket
{
    public readonly MessageID Type => MessageID.TileEntitySharing;
    public int ID;
    public bool IsNew;
    [Condition(nameof(IsNew), true)]
    [ExternalMemberValue(nameof(TileEntity.NetworkSend), true)]
    public TileEntity? Entity;
}