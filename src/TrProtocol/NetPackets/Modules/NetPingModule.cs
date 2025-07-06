using TrProtocol.Models;
using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetPingModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetPingModule;
    public Vector2 Position;
}
