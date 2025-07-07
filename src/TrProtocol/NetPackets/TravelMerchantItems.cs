using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct TravelMerchantItems : INetPacket
{
    public readonly MessageID Type => MessageID.TravelMerchantItems;
    [ArraySize(40)]
    public short[] ShopItems;
}