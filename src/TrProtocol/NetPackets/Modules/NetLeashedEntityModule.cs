using TrProtocol.Attributes;
using TrProtocol.Models;
using static Terraria.GameContent.LeashedEntity.NetModule;

namespace TrProtocol.NetPackets.Modules
{
    public partial struct NetLeashedEntityModule : INetModulesPacket
    {
        public readonly NetModuleType ModuleType => NetModuleType.NetLeashedEntityModule;

        public MessageType MessageType {
            get;
            set {
                Entity?.FullSync = value is MessageType.FullSync;
                field = value;
            }
        }

        [Int7BitEncoded]
        public int ID;
        public LeashedEntity Entity {
            get;
            set {
                field = value;
                field?.FullSync = MessageType is MessageType.FullSync;
            }
        }
    }
}