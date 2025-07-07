namespace TrProtocol.NetPackets;

public partial struct ClientSyncedInventory : INetPacket
{
    public readonly MessageID Type => MessageID.ClientSyncedInventory;
}