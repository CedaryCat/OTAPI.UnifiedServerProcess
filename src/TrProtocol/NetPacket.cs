using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using System.Text;

namespace TrProtocol {

    [PolymorphicBase(typeof(MessageID), nameof(Type))]
    public partial interface INetPacket : IAutoSerializable {
        public abstract MessageID Type { get; }
        public string? ToString() {
            return $"{{{Type}}}";
        }
    }
}
