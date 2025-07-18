﻿using Terraria.DataStructures;
using TrProtocol.Attributes;

namespace TrProtocol.Models.TileEntities;

public partial class TEItemFrame : TileEntity
{
    public sealed override TileEntityType EntityType => TileEntityType.TEItemFrame;
    [ExternalMember]
    [IgnoreSerialize]
    public sealed override bool NetworkSend { get; set; }
    [Condition(nameof(NetworkSend), false)]
    public sealed override int ID { get; set; }
    public sealed override Point16 Position { get; set; }

    public ItemData Item;
}
