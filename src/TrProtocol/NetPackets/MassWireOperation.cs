using Terraria.DataStructures;
using Terraria.GameContent.UI.WiresUI.Settings;

namespace TrProtocol.NetPackets;

public partial struct MassWireOperation : INetPacket
{
    public readonly MessageID Type => MessageID.MassWireOperation;
    public Point16 Start;
    public Point16 End;
    public MultiToolMode Mode;
}