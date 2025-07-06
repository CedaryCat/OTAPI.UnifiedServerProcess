using TrProtocol.Attributes;
using Terraria.DataStructures;

namespace TrProtocol.Models.TileEntities;

public partial class TETeleportationPylon : TileEntity {
    public sealed override TileEntityType EntityType => TileEntityType.TETeleportationPylon;
    [ExternalMember]
    [IgnoreSerialize]
    public sealed override bool NetworkSend { get; set; }
    [Condition(nameof(NetworkSend), false)]
    public sealed override int ID { get; set; }
    public sealed override Point16 Position { get; set; }
}
