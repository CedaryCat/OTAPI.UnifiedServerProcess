using Terraria.DataStructures;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.Models.TileEntities
{

    [PolymorphicBase(typeof(TileEntityType), nameof(EntityType))]
    public abstract partial class TileEntity : IAutoSerializable
    {
        public abstract TileEntityType EntityType { get; }
        [Condition(nameof(NetworkSend), false)]
        public abstract int ID { get; set; }
        public abstract Point16 Position { get; set; }
        [ExternalMember]
        [IgnoreSerialize]
        public abstract bool NetworkSend { get; set; }

        public abstract unsafe void ReadContent(ref void* ptr);

        public abstract unsafe void WriteContent(ref void* ptr);
    }
}
