using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets.Mobile;


public partial struct PlayerPlatformInfo : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerPlatformInfo;
    public byte PlayerSlot { get; set; }
    public Platform PlatformId;
}
