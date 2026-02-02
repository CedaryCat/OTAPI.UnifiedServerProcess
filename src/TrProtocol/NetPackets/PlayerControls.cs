using Microsoft.Xna.Framework;
using TrProtocol.Attributes;
using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerControls : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerControls;
    public byte PlayerSlot { get; set; }
    public PlayerControlData PlayerControlData;
    public PlayerMiscData1 PlayerMiscData1;
    public PlayerMiscData2 PlayerMiscData2;
    public PlayerMiscData3 PlayerMiscData3;
    public byte SelectedItem;
    public Vector2 Position;
    [Condition(nameof(PlayerMiscData1.HasVelocity), true)]
    public Vector2 Velocity;
    [Condition(nameof(PlayerMiscData1.IsMounting), true)]
    public ushort Mount;
    [Condition(nameof(PlayerMiscData2.CanReturnWithPotionOfReturn), true)]
    public Vector2 PotionOfReturnOriginalUsePosition;
    [Condition(nameof(PlayerMiscData2.CanReturnWithPotionOfReturn), true)]
    public Vector2 PotionOfReturnHomePosition;
    [Condition(nameof(PlayerMiscData3.HasNetCameraTarget))]
    public Vector2 NetCameraTarget;
}
