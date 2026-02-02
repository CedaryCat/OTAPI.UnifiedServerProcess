using Terraria.DataStructures;
using Terraria.GameContent;
using TrProtocol.Models;
using static Terraria.GameContent.NetModules.NetTeleportPylonModule;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetTeleportPylonModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetTeleportPylonModule;
    public SubPacketType PylonPacketType { get; set; }
    public Point16 Position { get; set; }
    public TeleportPylonType PylonType { get; set; }
}
