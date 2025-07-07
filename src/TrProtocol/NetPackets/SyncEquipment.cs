using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncEquipment : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncEquipment;
    public byte PlayerSlot { get; set; }
    public short ItemSlot;
    public short Stack;
    public byte Prefix;
    public short ItemType;
}
