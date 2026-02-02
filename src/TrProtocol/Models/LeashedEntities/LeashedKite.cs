using Microsoft.Xna.Framework;
using Terraria.DataStructures;
using TrProtocol.Attributes;

namespace TrProtocol.Models.LeashedEntities;

public partial class LeashedKite : LeashedEntity
{
    public sealed override LeashedEntityPrototype Prototype => LeashedEntityPrototype.LeashedKite;
    [Condition(nameof(FullSync))]
    public sealed override Point16 AnchorPosition { get; set; }
    [Condition(nameof(FullSync))]
    [Int7BitEncoded]
    public int ProjType;

    public Vector2 Position;
    public Vector2 Velocity;
    public byte Rotation;
    public float WindTarget;
    public float CloudAlpha;
    public float TimeCounter;
}
