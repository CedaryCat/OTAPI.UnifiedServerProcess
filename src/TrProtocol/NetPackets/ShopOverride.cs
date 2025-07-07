using Terraria;

namespace TrProtocol.NetPackets;

public partial struct ShopOverride : INetPacket
{
    public readonly MessageID Type => MessageID.ShopOverride;
    public byte ItemSlot;
    public short ItemType;
    public short Stack;
    public byte Prefix;
    public int Value;
    public BitsByte BuyOnce; // only first bit counts
}