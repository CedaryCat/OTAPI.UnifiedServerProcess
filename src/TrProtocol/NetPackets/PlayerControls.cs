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
    private bool HasValue => PlayerMiscData1.HasVelocity;
    public PlayerMiscData2 PlayerMiscData2;
    private bool CanReturnWithPotionOfReturn => PlayerMiscData2.CanReturnWithPotionOfReturn;
    public PlayerMiscData3 PlayerMiscData3;
    public byte SelectedItem;
    public Vector2 Position;
    [Condition(nameof(HasValue), true)]
    public Vector2 Velocity;
    [Condition(nameof(CanReturnWithPotionOfReturn), true)]
    public Vector2 PotionOfReturnOriginalUsePosition;
    [Condition(nameof(CanReturnWithPotionOfReturn), true)]
    public Vector2 PotionOfReturnHomePosition;
}
