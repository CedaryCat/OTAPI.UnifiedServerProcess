using TrProtocol.Models;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.NetModules;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetTeleportPylonModule : NetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetTeleportPylonModule;
    public NetTeleportPylonModule_SubPacketType PylonPacketType { get; set; }
    public Point16 Position { get; set; }
    public TeleportPylonType PylonType { get; set; }
}
