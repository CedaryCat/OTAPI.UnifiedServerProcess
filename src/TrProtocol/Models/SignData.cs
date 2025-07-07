using TrProtocol.Attributes;

namespace TrProtocol.Models
{
    public partial struct SignData
    {
        public readonly override string ToString() {
            return $"[{TileX}, {TileY}] {Text}";
        }
        public short ID;
        public short TileX;
        public short TileY;
        [IgnoreSerialize]
        private string? text;
        public string Text {
            readonly get => text ?? string.Empty;
            set => text = value;
        }
    }
}
