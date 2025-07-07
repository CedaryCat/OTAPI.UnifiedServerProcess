using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct UpdateTowerShieldStrengths : INetPacket
{
    public readonly MessageID Type => MessageID.UpdateTowerShieldStrengths;
    [ArraySize(4)]
    public ushort[] ShieldStrength { get; set; }
}