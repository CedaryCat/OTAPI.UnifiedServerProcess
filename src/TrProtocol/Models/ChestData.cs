using TrProtocol.Attributes;

namespace TrProtocol.Models
{
    public struct ChestData
    {
        public override readonly string ToString() {
            return $"[{TileX}, {TileY}] {Name}";
        }
        public short ID;
        public short TileX;
        public short TileY;
        [IgnoreSerialize]
        private string? name;
        public string Name {
            readonly get => name ?? string.Empty;
            set => name = value;
        }
    }
}
